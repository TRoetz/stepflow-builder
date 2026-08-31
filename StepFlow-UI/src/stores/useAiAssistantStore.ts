import { create } from 'zustand';
import { useAiModelConfigStore, type AiModelConfig } from '@stores/useAiModelConfigStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';

import { stepSchemas, schemaById } from '@schemas/index';
import { collectInScopeVariables } from '@utils/variables';
import { categoryPromptTemplates, summarizeConfigFields } from './aiAssistantPrompts';
// Strip trailing /v1 (or /v1/) so callers can append their own path segment
export function stripV1Suffix(url: string): string {
  return url.replace(/\/v1\/?$/, '');
}

// ── Types ──

export interface AiAssistantMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: number;
}

export interface WorkflowContext {
  nodeCount: number;
  edgeCount: number;
  selectedNodeId: string | null;
  selectedNodeName: string | null;
  selectedNodeSchemaId: string | null;
  selectedNodeCategory: string | null;
  selectedNodeConfigFields: string | null;
  // Rich per-state focus context (populated by setFocusedNode / focusNodeForAssistant)
  selectedNodeDescription: string | null;
  selectedNodeConfigValues: Record<string, unknown> | null;
  selectedNodeInputs: Array<{ label: string; type: string; optional: boolean }> | null;
  selectedNodeOutputs: Array<{ label: string; type: string; description?: string }> | null;
  /** {{token}} references resolvable from this node's upstream. */
  availableVariables: string[] | null;
  recentlyAddedNodeName: string | null;
  recentlyAddedNodeCategory: string | null;
  hasStartNode: boolean;
  hasEndNode: boolean;
}

// AI-generated flow JSON structure
export interface AiFlowNode {
  schemaId: string;
  label: string;
  config?: Record<string, unknown>;
  position?: { x: number; y: number };
}

export interface AiFlowEdge {
  source: string; // node label
  target: string; // node label
}

export interface AiFlowJson {
  name?: string;
  description?: string;
  nodes: AiFlowNode[];
  edges: AiFlowEdge[];
}

interface AiAssistantState {
  // Chat state
  messages: AiAssistantMessage[];
  isOpen: boolean;
  isCollapsed: boolean;
  isTyping: boolean;

  // Workflow context
  context: WorkflowContext;

  // Actions
  addMessage: (role: 'user' | 'assistant' | 'system', content: string) => void;
  clearChat: () => void;
  toggleOpen: () => void;
  toggleCollapsed: () => void;
  setTyping: (typing: boolean) => void;

  // Context actions
  updateContext: (partial: Partial<WorkflowContext>) => void;
  notifyNodeAdded: (nodeName: string, category: string) => void;
  /** Recompute rich per-state context for a selected node; null clears it. */
  setFocusedNode: (nodeId: string | null) => void;
  /** Focus a state in the assistant: sets context, opens panel, posts orientation message. */
  focusNodeForAssistant: (nodeId: string) => void;

  // AI response generation
  generateResponse: (userMessage: string) => Promise<string>;
}

const defaultContext: WorkflowContext = {
  nodeCount: 0,
  edgeCount: 0,
  selectedNodeId: null,
  selectedNodeName: null,
  selectedNodeSchemaId: null,
  selectedNodeCategory: null,
  selectedNodeConfigFields: null,
  selectedNodeDescription: null,
  selectedNodeConfigValues: null,
  selectedNodeInputs: null,
  selectedNodeOutputs: null,
  availableVariables: null,
  recentlyAddedNodeName: null,
  recentlyAddedNodeCategory: null,
  hasStartNode: false,
  hasEndNode: false,
};

// ── Context helpers ──

function buildContextSummary(ctx: WorkflowContext): string {
  const parts: string[] = [];
  parts.push(`Canvas has ${ctx.nodeCount} node${ctx.nodeCount !== 1 ? 's' : ''} and ${ctx.edgeCount} connection${ctx.edgeCount !== 1 ? 's' : ''}`);
  if (ctx.selectedNodeName) {
    parts.push(`Selected: ${ctx.selectedNodeName}`);
  }
  if (ctx.recentlyAddedNodeName) {
    parts.push(`Recently added: ${ctx.recentlyAddedNodeName} (${ctx.recentlyAddedNodeCategory})`);
  }
  if (!ctx.hasStartNode) {
    parts.push('No START node on canvas');
  }
  if (ctx.hasStartNode && !ctx.hasEndNode) {
    parts.push('No END node on canvas');
  }
  return parts.join('. ');
}

function buildSchemaReference(): string {
  // Ultra-compact: only schemaId and name, one per line
  return stepSchemas
    .map((s) => `- "${s.schemaId}": ${s.name}`)
    .join('\n');
}

// ── AI API Integration ──

function buildAssistantSystemPrompt(ctx: WorkflowContext): string {
  const parts: string[] = [
    'You are a StepFlow Builder assistant.',
    '',
    '## MODES:',
    '- When asked to BUILD or MODIFY a flow → output ONLY the JSON described below, wrapped in ```json. No explanation, no reasoning, no extra text.',
    '- For questions about states, configuration, syntax or usage (especially the FOCUSED STATE) → answer in concise markdown with code blocks where helpful. Do NOT emit flow JSON unless explicitly asked to build or modify a flow.',
    '',
    '## FLOW JSON FORMAT:',
    '{',
    '  "name": "Flow Name",',
    '  "description": "What it does",',
    '  "nodes": [',
    '    { "schemaId": "stepflow:terminal:start", "label": "START" },',
    '    { "schemaId": "stepflow:api:http", "label": "Get Data", "config": { "url": "https://api.example.com", "method": "GET" } },',
    '    { "schemaId": "stepflow:terminal:end", "label": "END" }',
    '  ],',
    '  "edges": [',
    '    { "source": "START", "target": "Get Data" },',
    '    { "source": "Get Data", "target": "END" }',
    '  ]',
    '}',
    '',
    '## FLOW RULES:',
    '- ALWAYS start with "stepflow:terminal:start" (label: "START")',
    '- ALWAYS end with "stepflow:terminal:end" (label: "END")',
    '- Each node MUST have a unique "label" (used for edge wiring)',
    '- Edges reference node "label" values, NOT schemaId',
    '- "config" keys must match schema field "id" values',
    '- If ambiguous, make reasonable defaults.',
    '- NEVER output a file download link or a file path.',
    '',
    '## AVAILABLE NODES (schemaId: name):',
    buildSchemaReference(),
    '',
    '## CANVAS:',
    buildContextSummary(ctx),
  ];

  if (ctx.selectedNodeName) {
    parts.push(`Selected: "${ctx.selectedNodeName}"`);
  }

  // Rich per-state focus context — present when the user asked for help with a specific state.
  if (ctx.selectedNodeDescription !== null && ctx.selectedNodeName) {
    const schema = stepSchemas.find((s) => s.schemaId === ctx.selectedNodeSchemaId);
    parts.push('', '## FOCUSED STATE — the user is asking for help configuring this specific state:');
    parts.push(`- Name: ${ctx.selectedNodeName} (${ctx.selectedNodeSchemaId || 'unknown schema'}) [category: ${schema?.category ?? 'unknown'}]`);
    parts.push(`- Purpose: ${ctx.selectedNodeDescription}`);

    if (schema) {
      const guidance = categoryPromptTemplates[schema.category](summarizeConfigFields(schema.configFields));
      if (guidance.trim()) parts.push('', guidance.trim());

      const fields = schema.configFields
        .map((f) => {
          const req = f.required ? ' [required]' : '';
          const def = f.default !== undefined && String(f.default) !== '' ? ` [default: ${JSON.stringify(f.default)}]` : '';
          return `  - ${f.id} (${f.type})${req}${def}: ${f.description || 'no description'}`;
        })
        .join('\n');
      if (fields) parts.push('- Configuration fields:', fields);

      const inputs = schema.inputs.map((p) => `${p.label} (${p.type}${p.optional ? ', optional' : ''})`).join(', ');
      if (inputs) parts.push(`- Input ports: ${inputs}`);

      const outputs = schema.outputs.map((p) => `  - ${p.label} (${p.type})${p.description ? ` — ${p.description}` : ''}`).join('\n');
      if (outputs) parts.push('- Output ports:', outputs);
    }

    if (ctx.selectedNodeConfigValues !== null) {
      parts.push(`- Current values: ${JSON.stringify(ctx.selectedNodeConfigValues)}`);
    }

    const vars = ctx.availableVariables ?? [];
    if (vars.length > 0) {
      parts.push(`- Variables available upstream (reference as {{token}}): ${vars.join(', ')}`);
    } else {
      parts.push('- No upstream variables are connected yet.');
    }

    parts.push(
      '',
      '## FOCUSED-STATE RULES:',
      '- "How do I use this state?" → explain its purpose, walk through each configuration field in order, and finish with a complete worked example using THIS node\'s actual inputs.',
      '- "Check my inputs/outputs" → validate every current value against the available variables and port types; list each problem with the exact fix, or say explicitly that everything is valid.',
      '- Syntax questions → answer with exact working examples in code blocks using this state\'s real field ids and variable tokens.'
    );
  }

  return parts.join('\n');
}

interface AiApiMessage {
  role: 'system' | 'user' | 'assistant';
  content: string;
}

interface AiApiResponse {
  choices?: Array<{
    message?: {
      content?: string;
    };
  }>;
  error?: {
    message?: string;
  };
}

export async function callAiApi(
  config: AiModelConfig,
  systemPrompt: string,
  userMessage: string,
): Promise<string> {
  const messages: AiApiMessage[] = [
    { role: 'system', content: systemPrompt },
    { role: 'user', content: userMessage },
  ];

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };

  const localProviders = ['ollama', 'lmStudio', 'llamaCpp'] as const;
  const isLocalProvider = localProviders.includes(config.provider as (typeof localProviders)[number]);

  // Set authentication based on provider
  if (config.provider === 'azureOpenAI') {
    headers['api-key'] = config.apiKey;
  } else if (config.provider === 'anthropic') {
    headers['x-api-key'] = config.apiKey;
    headers['anthropic-version'] = '2023-06-01';
  } else if (!isLocalProvider && config.apiKey) {
    headers['Authorization'] = `Bearer ${config.apiKey}`;
  }

  // Build request body based on provider
  let body: Record<string, unknown>;
  let url: string;

  if (config.provider === 'anthropic') {
    body = {
      model: config.defaultModel,
      system: systemPrompt,
      messages: messages.filter((m) => m.role !== 'system'),
      max_tokens: config.maxTokens,
      temperature: config.temperature,
      top_p: config.topP,
    };
    url = `${stripV1Suffix(config.baseUrl)}/v1/messages`;
  } else {
    const basePath = config.provider === 'azureOpenAI'
      ? `${stripV1Suffix(config.baseUrl)}/openai/deployments/${config.defaultModel}`
      : `${stripV1Suffix(config.baseUrl)}/v1`;

    body = {
      model: config.defaultModel,
      messages,
      max_tokens: config.maxTokens,
      temperature: config.temperature,
      top_p: config.topP,
    };
    url = `${basePath}/chat/completions`;
  }

  const response = await fetch(url, {
    method: 'POST',
    headers,
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const errorData = (await response.json()) as AiApiResponse;
    throw new Error(errorData.error?.message ?? `API error: ${response.status}`);
  }

  const data = (await response.json()) as Record<string, unknown>;
  console.log('AI API response keys:', Object.keys(data));

  // Extract response based on provider
  if (config.provider === 'anthropic') {
    if (data && typeof data === 'object' && 'content' in data && Array.isArray(data.content)) {
      const content = data.content as Array<{ text?: string }>;
      if (content?.[0]?.text) return content[0].text;
    }
    throw new Error('No content in Anthropic response');
  }

  // OpenAI-compatible extraction — inspect choices
  const choices = data.choices as Array<Record<string, unknown>> | undefined;
  console.log('AI API choices:', JSON.stringify(choices));

  // Check for truncation — model ran out of tokens
  const finishReason = (choices?.[0] as { finish_reason?: string } | undefined)?.finish_reason;
  if (finishReason === 'length') {
    const reasoningLen = String((choices?.[0] as { message?: { reasoning_content?: string } } | undefined)?.message?.reasoning_content ?? '').length;
    const hint = reasoningLen > 100
      ? ' Model spent tokens on internal reasoning. Try increasing maxTokens (current: ' + config.maxTokens + ') to at least ' + Math.max(config.maxTokens * 2, 8192) + ', or use a non-reasoning model.'
      : ' Try increasing maxTokens from ' + config.maxTokens + ' to at least ' + Math.max(config.maxTokens * 2, 4096) + '.';
    throw new Error('Model response was truncated (hit token limit).' + hint);
  }

  // Standard: choices[0].message.content
  const standard = (data as { choices?: Array<{ message?: { content?: string | null } }> }).choices?.[0]?.message?.content;
  if (typeof standard === 'string' && standard.length > 0) return standard;

  // Reasoning models (Qwen, DeepSeek): content may be empty, reasoning in reasoning_content
  const reasoningContent = (choices?.[0] as { message?: { reasoning_content?: string | null } } | undefined)?.message?.reasoning_content;
  if (typeof reasoningContent === 'string' && reasoningContent.length > 0 && (!standard || !standard.trim())) {
    // Model produced reasoning but no content — extract JSON from reasoning if present
    const jsonMatch = reasoningContent.match(/```(?:json)?\s*\n([\s\S]*?)\n```/);
    if (jsonMatch) {
      return jsonMatch[1].trim();
    }
    // Fall through to error — model reasoned but didn't produce output
  }

  // Some servers return delta content (streaming-style): choices[0].delta.content
  const delta = (data as { choices?: Array<{ delta?: { content?: string | null } }> }).choices?.[0]?.delta?.content;
  if (typeof delta === 'string' && delta.length > 0) return delta;

  // Some servers return content at top level
  if (typeof data.content === 'string' && data.content.length > 0) return data.content;

  // Some servers return response at top level
  if (typeof data.response === 'string' && data.response.length > 0) return data.response;

  // Log the full shape for debugging and fail
  const choicesDetail = choices ? `choices[${choices.length}]` : 'no choices';
  const firstChoice = choices?.[0] ? JSON.stringify(choices[0]).slice(0, 500) : 'none';
  console.error('AI API full response:', JSON.stringify(data).slice(0, 2000));
  throw new Error(`No content in API response. ${choicesDetail}: ${firstChoice}`);
}

export async function testAiConnection(config: AiModelConfig): Promise<{ success: boolean; message: string }> {
  const localProviders = ['ollama', 'lmStudio', 'llamaCpp'] as const;
  const isLocalProvider = localProviders.includes(config.provider as (typeof localProviders)[number]);

  if (!config.baseUrl) {
    return { success: false, message: 'Please enter an API Base URL.' };
  }
  if (!isLocalProvider && !config.apiKey) {
    return { success: false, message: 'Please enter an API key for this provider.' };
  }

  try {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };

    if (config.provider === 'azureOpenAI') {
      headers['api-key'] = config.apiKey;
    } else if (!isLocalProvider && config.apiKey) {
      headers['Authorization'] = `Bearer ${config.apiKey}`;
    }

    const body = {
      model: config.defaultModel,
      messages: [{ role: 'user', content: 'Hi' }],
      max_tokens: 1,
    };

    const basePath = config.provider === 'azureOpenAI'
      ? `${stripV1Suffix(config.baseUrl)}/openai/deployments/${config.defaultModel}`
      : `${stripV1Suffix(config.baseUrl)}/v1`;

    const response = await fetch(`${basePath}/chat/completions`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
    });

    if (response.ok) {
      return { success: true, message: 'Connection successful!' };
    }

    const errorData = (await response.json()) as AiApiResponse;
    return { success: false, message: errorData.error?.message ?? `API error: ${response.status}` };
  } catch (error) {
    if (error instanceof TypeError && error.message.includes('fetch')) {
      const hint =
        config.provider === 'lmStudio'
          ? ' In LM Studio, open Developer → Server settings and enable "Allow cross-origin requests" (CORS), then restart the server.'
          : config.provider === 'ollama'
            ? ' Ollama normally allows browser access; make sure it is running on this machine with default CORS enabled.'
            : ' Add --host 0.0.0.0 and CORS headers to your server config.';
      return {
        success: false,
        message: `CORS error: Your AI server at ${config.baseUrl} is blocking browser requests.${hint}`,
      };
    }
    return { success: false, message: `Connection failed: ${error instanceof Error ? error.message : String(error)}` };
  }
}

// ── JSON Flow Parsing & Loading ──

function extractJsonFromResponse(text: string): string {
  let cleaned = text.trim();

  // Try extracting markdown block wrapped in ```json ... ``` or ``` ... ```
  const fenceMatch = cleaned.match(/```(?:json)?\s*\n([\s\S]*?)\n```/i);
  if (fenceMatch) {
    cleaned = fenceMatch[1].trim();
  } else {
    // Look for outermost JSON object { ... } if text surrounds it
    const startIdx = cleaned.indexOf('{');
    const endIdx = cleaned.lastIndexOf('}');
    if (startIdx !== -1 && endIdx > startIdx) {
      cleaned = cleaned.slice(startIdx, endIdx + 1);
    }
  }
  return cleaned;
}

// Normalize common AI typos and state machine variations in JSON before parsing
function normalizeFlowJson(jsonStr: string): string {
  return jsonStr
    .replace(/"schemald"\s*:/g, '"schemaId":')
    .replace(/"schema_id"\s*:/g, '"schemaId":')
    .replace(/"StartAt"\s*:/g, '"startAt":')
    .replace(/"States"\s*:/g, '"states":')
    .replace(/"Resource"\s*:/g, '"resource":')
    .replace(/"Next"\s*:/g, '"next":')
    .replace(/"Type"\s*:/g, '"type":')
    .replace(/"Comment"\s*:/g, '"comment":')
    .replace(/"Parameters"\s*:/g, '"parameters":');
}

interface ParseResult {
  ok: boolean;
  flow?: AiFlowJson;
  error?: string;
}

/**
 * Infer stepflow schemaId from Amazon States Language / State Machine state object.
 */
function stateToSchemaId(state: Record<string, unknown>): string {
  const resource = typeof state.resource === 'string' ? state.resource : '';
  const type = typeof state.type === 'string' ? state.type : '';

  if (resource.startsWith('ai://') || resource.includes('ai')) return 'stepflow:ai:decision';
  if (resource.startsWith('rule://') || resource.includes('rule')) return 'stepflow:rule:rule_engine';
  if (resource.startsWith('sql://')) return 'stepflow:data:sql';
  if (resource.startsWith('duckdb://')) return 'stepflow:data:duckdb';
  if (resource.startsWith('eav://')) return 'stepflow:data:eav';
  if (resource.startsWith('http://') || resource.startsWith('https://')) return 'stepflow:api:http';
  if (resource.startsWith('api://')) return 'stepflow:api:registered';
  if (resource.startsWith('transform://jsonata')) return 'stepflow:transform:jsonata';
  if (resource.startsWith('transform://')) return 'stepflow:transform:script';
  if (resource.startsWith('flow://')) return 'stepflow:subflow:invoke';

  switch (type.toLowerCase()) {
    case 'choice': return 'stepflow:flow:choice';
    case 'map': return 'stepflow:flow:map';
    case 'parallel': return 'stepflow:flow:parallel';
    case 'succeed': return 'stepflow:flow:succeed';
    case 'fail': return 'stepflow:flow:fail';
    case 'wait': return 'stepflow:utility:wait';
    case 'pass': return 'stepflow:utility:pass';
    default: return 'stepflow:utility:pass';
  }
}

/**
 * Convert Amazon States Language (StateMachineDefinition / { startAt, states }) into AiFlowJson.
 */
function convertStateMachineToAiFlow(parsed: Record<string, unknown>): AiFlowJson {
  const states = (parsed.states || {}) as Record<string, Record<string, unknown>>;
  const nodes: AiFlowNode[] = [];
  const edges: AiFlowEdge[] = [];

  const stateKeys = Object.keys(states);
  const startAt = typeof parsed.startAt === 'string' ? parsed.startAt : stateKeys[0];

  // If startAt doesn't exist or doesn't map to a START terminal node, prepend a START terminal if not present
  const hasStartTerminal = stateKeys.some(key => {
    const s = states[key];
    const res = typeof s?.resource === 'string' ? s.resource : '';
    return key.toUpperCase() === 'START' || res.includes('start');
  });

  if (!hasStartTerminal) {
    nodes.push({
      schemaId: 'stepflow:terminal:start',
      label: 'START',
    });
  }

  for (const stateName of stateKeys) {
    const state = states[stateName];
    let schemaId = stateToSchemaId(state);

    if (stateName.toUpperCase() === 'START') {
      schemaId = 'stepflow:terminal:start';
    } else if (stateName.toUpperCase() === 'END') {
      schemaId = 'stepflow:terminal:end';
    }

    const config = (state.parameters || state.config) as Record<string, unknown> | undefined;

    nodes.push({
      schemaId,
      label: stateName,
      config: config ?? (typeof state.comment === 'string' ? { description: state.comment } : undefined),
    });

    // Handle next edge
    if (typeof state.next === 'string' && state.next) {
      edges.push({
        source: stateName,
        target: state.next,
      });
    }

    // Handle Choice state choices
    if (Array.isArray(state.choices)) {
      for (const choice of state.choices as Array<Record<string, unknown>>) {
        if (typeof choice.next === 'string' && choice.next) {
          edges.push({
            source: stateName,
            target: choice.next,
          });
        }
      }
    }
  }

  // Connect START to startAt if START node was prepended
  if (!hasStartTerminal && startAt && states[startAt]) {
    edges.unshift({
      source: 'START',
      target: startAt,
    });
  }

  return {
    name: typeof parsed.name === 'string' ? parsed.name : (typeof parsed.comment === 'string' ? parsed.comment : 'Generated Flow'),
    description: typeof parsed.description === 'string' ? parsed.description : undefined,
    nodes,
    edges,
  };
}
function parseAiFlowJson(text: string): ParseResult {
  try {
    const jsonStr = normalizeFlowJson(extractJsonFromResponse(text));
    const parsed = JSON.parse(jsonStr) as Record<string, unknown>;

    // Case 1: Standard AiFlowJson format ({ nodes: [...], edges: [...] })
    if (Array.isArray(parsed.nodes) && Array.isArray(parsed.edges)) {
      for (let i = 0; i < parsed.nodes.length; i++) {
        const node = parsed.nodes[i] as Record<string, unknown>;
        if (!node.schemaId) {
          return { ok: false, error: `Node #${i + 1} is missing "schemaId".` };
        }
        if (!node.label) {
          return { ok: false, error: `Node #${i + 1} is missing "label".` };
        }
      }
      return { ok: true, flow: parsed as unknown as AiFlowJson };
    }

    // Case 2: State Machine / ASL format ({ startAt: "...", states: { ... } })
    if (parsed.states && typeof parsed.states === 'object') {
      const convertedFlow = convertStateMachineToAiFlow(parsed);
      return { ok: true, flow: convertedFlow };
    }

    // Case 3: AI output a file download link or file path instead of JSON
    if (typeof parsed.url === 'string' || typeof parsed.path === 'string' || typeof parsed.file === 'string') {
      return { ok: false, error: 'AI output a file link instead of JSON. Please try rephrasing your request.' };
    }

    return { ok: false, error: 'JSON response must contain either a "nodes" array or a "states" dictionary.' };
  } catch {
    return { ok: false, error: 'Failed to parse JSON from AI response. The output was not valid JSON.' };
  }
}

function loadAiFlow(flow: AiFlowJson): void {
  const addNode = useNodeStore.getState().addNode;
  const updateNodeData = useNodeStore.getState().updateNodeData;
  const addEdge = useEdgeStore.getState().addEdge;

  // Calculate starting position
  const allNodes = useNodeStore.getState().nodes;
  let startY = 150;
  if (allNodes.length > 0) {
    const maxY = Math.max(...allNodes.map(n => n.position.y));
    startY = maxY + 150;
  }
  const startX = 250;

  // Track label → node id mapping for edge wiring
  const labelToId = new Map<string, string>();

  // Create nodes
  for (const nodeDef of flow.nodes) {
    const position = nodeDef.position ?? { x: startX, y: startY };
    addNode(nodeDef.schemaId, position);

    // addNode appends to the array — grab the last entry
    const newNodes = useNodeStore.getState().nodes;
    const newNode = newNodes[newNodes.length - 1];
    if (newNode) {
      labelToId.set(nodeDef.label, newNode.id);

      // Update node label and config
      const updates: Record<string, unknown> = { label: nodeDef.label };
      if (nodeDef.config) {
        updates.configuration = { ...newNode.data.configuration, ...nodeDef.config };
      }
      updateNodeData(newNode.id, updates);
    }

    startY += 150;
  }

  // Create edges
  for (const edgeDef of flow.edges) {
    const sourceId = labelToId.get(edgeDef.source);
    const targetId = labelToId.get(edgeDef.target);

    if (sourceId && targetId) {
      addEdge({
        id: `edge-${sourceId}-${targetId}`,
        source: sourceId,
        target: targetId,
        type: 'step-edge',
      });
    }
  }
}

// ── Store ──

export const useAiAssistantStore = create<AiAssistantState>((set, get) => ({
  messages: [
    {
      id: 'welcome',
      role: 'assistant',
      content: 'Welcome to StepFlow Builder! I\'m your AI assistant. Ask me to **build a flow** and I\'ll generate the workflow JSON and load it onto the canvas. You can also ask me questions about configuring nodes, control flow patterns, or best practices.',
      timestamp: Date.now(),
    },
  ],
  isOpen: false,
  isCollapsed: false,
  isTyping: false,
  context: defaultContext,

  addMessage: (role, content) => {
    const msg: AiAssistantMessage = {
      id: `msg-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      role,
      content,
      timestamp: Date.now(),
    };
    set((state) => ({ messages: [...state.messages, msg] }));
    return msg;
  },

  clearChat: () => {
    set({
      messages: [
        {
          id: 'welcome',
          role: 'assistant',
          content: 'Chat cleared. Ask me to build a flow or help with configuration.',
          timestamp: Date.now(),
        },
      ],
    });
  },

  toggleOpen: () => set((s) => ({ isOpen: !s.isOpen })),
  toggleCollapsed: () => set((s) => ({ isCollapsed: !s.isCollapsed })),
  setTyping: (typing) => set({ isTyping: typing }),

  updateContext: (partial) =>
    set((state) => ({ context: { ...state.context, ...partial } })),

  notifyNodeAdded: (nodeName, category) => {
    const ctx = get().context;
    const updatedContext = {
      ...ctx,
      recentlyAddedNodeName: nodeName,
      recentlyAddedNodeCategory: category,
      nodeCount: ctx.nodeCount + 1,
      hasStartNode: nodeName === 'START' || ctx.hasStartNode,
      hasEndNode: nodeName === 'END' || ctx.hasEndNode,
    };
    set({ context: updatedContext });
  },

  setFocusedNode: (nodeId) => {
    const clear = {
      selectedNodeId: null,
      selectedNodeName: null,
      selectedNodeSchemaId: null,
      selectedNodeCategory: null,
      selectedNodeConfigFields: null,
      selectedNodeDescription: null,
      selectedNodeConfigValues: null,
      selectedNodeInputs: null,
      selectedNodeOutputs: null,
      availableVariables: null,
    };
    if (!nodeId) {
      set((state) => ({ context: { ...state.context, ...clear } }));
      return;
    }
    const nodes = useNodeStore.getState().nodes;
    const node = nodes.find((n) => n.id === nodeId);
    if (!node) return;
    const data = node.data as { schemaId?: string; label?: string; configuration?: Record<string, unknown> } | undefined;
    const schemaId = data?.schemaId ?? null;
    const schema = schemaId ? schemaById.get(schemaId) : undefined;
    const configValues = data?.configuration && Object.keys(data.configuration).length > 0 ? { ...data.configuration } : {};
    set((state) => ({
      context: {
        ...state.context,
        selectedNodeId: nodeId,
        selectedNodeName: data?.label || null,
        selectedNodeSchemaId: schemaId,
        selectedNodeCategory: schema?.category ?? null,
        selectedNodeConfigFields: schema ? summarizeConfigFields(schema.configFields) : null,
        selectedNodeDescription: schema?.description ?? null,
        selectedNodeConfigValues: configValues,
        selectedNodeInputs: schema ? schema.inputs.map((p) => ({ label: p.label, type: p.type, optional: !!p.optional })) : null,
        selectedNodeOutputs: schema ? schema.outputs.map((p) => ({ label: p.label, type: p.type, description: p.description })) : null,
        availableVariables: collectInScopeVariables(nodeId, nodes, useEdgeStore.getState().edges).map((v) => `{{${v.token}}}`),
      },
    }));
  },

  focusNodeForAssistant: (nodeId) => {
    get().setFocusedNode(nodeId);
    set({ isOpen: true, isCollapsed: false });
    const name = get().context.selectedNodeName;
    if (name) {
      get().addMessage(
        'system',
        `Now helping with **${name}**. Ask how to use it, ask me to check your inputs & outputs, or request the exact syntax.`
      );
    }
  },

  generateResponse: async (userMessage: string) => {
    set({ isTyping: true });

    const ctx = get().context;
    const config: AiModelConfig = {
      provider: useAiModelConfigStore.getState().provider,
      baseUrl: useAiModelConfigStore.getState().baseUrl,
      apiKey: useAiModelConfigStore.getState().apiKey,
      defaultModel: useAiModelConfigStore.getState().defaultModel,
      temperature: useAiModelConfigStore.getState().temperature,
      maxTokens: useAiModelConfigStore.getState().maxTokens,
      topP: useAiModelConfigStore.getState().topP,
    };

    // Check if AI is configured
    const localProviders = ['ollama', 'lmStudio', 'llamaCpp'] as const;
    const isLocalProvider = localProviders.includes(config.provider as (typeof localProviders)[number]);
    const shouldCallApi = (config.apiKey && config.baseUrl) || (isLocalProvider && config.baseUrl);

    if (!shouldCallApi) {
      set({ isTyping: false });
      const tip = !config.baseUrl
        ? 'Configure your AI provider base URL in settings (gear icon in the header).'
        : !isLocalProvider && !config.apiKey
          ? 'Enter an API key for your provider in settings.'
          : '';
      return `⚠️ **AI not configured.** I need an AI provider to generate flows.\n\n💡 ${tip}`;
    }

    // Build system prompt with workflow context and schema reference
    const systemPrompt = buildAssistantSystemPrompt(ctx);

    try {
      const response = await callAiApi(config, systemPrompt, userMessage);

      // Try to parse as flow JSON
      const parseResult = parseAiFlowJson(response);
      if (parseResult.ok && parseResult.flow) {
        const flowJson = parseResult.flow;
        loadAiFlow(flowJson);

        // Update context to reflect new nodes
        const newNodes = useNodeStore.getState().nodes;
        const newEdges = useEdgeStore.getState().edges;
        set((state) => ({
          isTyping: false,
          context: {
            ...state.context,
            nodeCount: newNodes.length,
            edgeCount: newEdges.length,
            hasStartNode: newNodes.some(n => n.data?.schemaId === 'stepflow:terminal:start') || state.context.hasStartNode,
            hasEndNode: newNodes.some(n => n.data?.schemaId === 'stepflow:terminal:end') || state.context.hasEndNode,
          },
        }));

        const nodeCount = flowJson.nodes.length;
        const edgeCount = flowJson.edges.length;
        const flowName = flowJson.name || 'Flow';
        return `✅ **Flow "${flowName}" loaded!**\n\nAdded **${nodeCount} nodes** and **${edgeCount} connections** to the canvas.`;
      }

      // JSON parse failed — report the error
      if (parseResult.error) {
        set({ isTyping: false });
        return `⚠️ **Flow JSON invalid:** ${parseResult.error}\n\nPlease try rephrasing your request.`;
      }

      // Not a flow JSON — return the AI's text response as-is
      set({ isTyping: false });
      return response;
    } catch (error) {
      set({ isTyping: false });
      const errorMsg = error instanceof Error ? error.message : String(error);
      return `⚠️ **AI request failed:** ${errorMsg}\n\nCheck your AI provider configuration (gear icon).`;
    }
  },
}));
