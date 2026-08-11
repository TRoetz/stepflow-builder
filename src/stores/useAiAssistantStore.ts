import { create } from 'zustand';
import { useAiModelConfigStore, type AiModelConfig } from '@stores/useAiModelConfigStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { summarizeConfigFields } from '@stores/aiAssistantPrompts';
import { stepSchemas } from '@schemas/index';


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
  notifyNodeSelected: (nodeName: string | null, schemaId?: string, category?: string, configFieldsSummary?: string) => void;

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
  return stepSchemas
    .map((s) => {
      const configSummary = s.configFields.length > 0
        ? `  config_fields: ${summarizeConfigFields(s.configFields)}`
        : '';
      return `- "${s.schemaId}" (${s.name}, ${s.category}): ${s.description}${configSummary ? '\n' + configSummary : ''}`;
    })
    .join('\n');
}

// ── AI API Integration ──

function buildAssistantSystemPrompt(ctx: WorkflowContext): string {
  const parts: string[] = [
    'You are the AI assistant for StepFlow Builder, a visual workflow designer.',
    '',
    '## PRIMARY TASK: Generate Flow JSON',
    'When the user asks you to build, create, design, or describe a workflow/flow, respond with ONLY a JSON object in this exact format:',
    '',
    '```json',
    '{',
    '  "name": "Flow Name",',
    '  "description": "What this flow does",',
    '  "nodes": [',
    '    { "schemaId": "stepflow:terminal:start", "label": "START" },',
    '    { "schemaId": "stepflow:ai:decision", "label": "Analyze Input", "config": { "prompt": "Analyze the input data", "model": "gpt-4" } },',
    '    { "schemaId": "stepflow::end", "label": "END" }',
    '  ],',
    '  "edges": [',
    '    { "source": "START", "target": "Analyze Input" },',
    '    { "source": "Analyze Input", "target": "END" }',
    '  ]',
    '}',
    '```',
    '',
    '### Available Node Types (schemaId → name):',
    buildSchemaReference(),
    '',
    '### Rules for JSON Output:',
    '- ALWAYS include a START node (schemaId: "stepflow:terminal:start") as the first node.',
    '- ALWAYS include at least one END node (schemaId: "stepflow::end") as the last node.',
    '- Each node MUST have a unique "label" — labels are used to wire edges.',
    '- Edges use node "label" values for "source" and "target" (not schemaId).',
    '- Use "config" to set node-specific properties (see schema fields above).',
    '- Keep "config" keys matching the schema field "id" values.',
    '- Return ONLY the JSON. No markdown, no explanation, no extra text.',
    '- If the request is ambiguous, make reasonable defaults and build the flow.',
    '',
    '### Non-Flow Questions:',
    'If the user asks a question about configuration, best practices, or how to use the builder (not asking to build a flow), answer normally in markdown.',
    '',
    `## CURRENT CANVAS CONTEXT:`,
    buildContextSummary(ctx),
  ];

  if (ctx.selectedNodeName) {
    parts.push(`Currently selected node: "${ctx.selectedNodeName}"`);
    if (ctx.selectedNodeCategory) {
      parts.push(`Category: ${ctx.selectedNodeCategory}`);
    }
    if (ctx.selectedNodeConfigFields) {
      parts.push(`Config fields: ${ctx.selectedNodeConfigFields}`);
    }
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

async function callAiApi(
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
    url = `${config.baseUrl}/v1/messages`;
  } else {
    const basePath = config.provider === 'azureOpenAI'
      ? `${config.baseUrl}/openai/deployments/${config.defaultModel}`
      : `${config.baseUrl}/v1`;

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

  const data = (await response.json()) as AiApiResponse;

  // Extract response based on provider
  if (config.provider === 'anthropic') {
    if (data && typeof data === 'object' && 'content' in data && Array.isArray(data.content)) {
      const content = data.content as Array<{ text?: string }>;
      if (content?.[0]?.text) return content[0].text;
    }
    throw new Error('No content in Anthropic response');
  }

  const content = data.choices?.[0]?.message?.content;
  if (content) return content;
  throw new Error('No content in API response');
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
      ? `${config.baseUrl}/openai/deployments/${config.defaultModel}`
      : `${config.baseUrl}/v1`;

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
      return {
        success: false,
        message: `CORS error: Your AI server at ${config.baseUrl} is blocking browser requests. Add --host 0.0.0.0 and CORS headers to your server config.`,
      };
    }
    return { success: false, message: `Connection failed: ${error instanceof Error ? error.message : String(error)}` };
  }
}

// ── JSON Flow Parsing & Loading ──

function extractJsonFromResponse(text: string): string {
  // Strip markdown code fences if present
  let cleaned = text.trim();
  const fenceMatch = cleaned.match(/```(?:json)?\s*\n([\s\S]*?)\n```/);
  if (fenceMatch) {
    cleaned = fenceMatch[1].trim();
  }
  return cleaned;
}

function parseAiFlowJson(text: string): AiFlowJson | null {
  try {
    const jsonStr = extractJsonFromResponse(text);
    const parsed = JSON.parse(jsonStr) as AiFlowJson;
    if (!parsed.nodes || !Array.isArray(parsed.nodes)) return null;
    if (!parsed.edges || !Array.isArray(parsed.edges)) return null;
    return parsed;
  } catch {
    return null;
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

  notifyNodeSelected: (nodeName, schemaId, category, configFieldsSummary) => {
    set((state) => ({
      context: {
        ...state.context,
        selectedNodeName: nodeName,
        selectedNodeSchemaId: schemaId ?? null,
        selectedNodeCategory: category ?? null,
        selectedNodeConfigFields: configFieldsSummary ?? null,
      },
    }));
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
      const flowJson = parseAiFlowJson(response);
      if (flowJson) {
        // Load the flow onto the canvas
        loadAiFlow(flowJson);

        // Update context to reflect new nodes
        const newNodes = useNodeStore.getState().nodes;
        const newEdges = useEdgeStore.getState().edges;
        set((state) => ({
          context: {
            ...state.context,
            nodeCount: newNodes.length,
            edgeCount: newEdges.length,
            hasStartNode: newNodes.some(n => n.data?.schemaId === 'stepflow:terminal:start') || state.context.hasStartNode,
            hasEndNode: newNodes.some(n => n.data?.schemaId === 'stepflow::end') || state.context.hasEndNode,
          },
        }));

        const nodeCount = flowJson.nodes.length;
        const edgeCount = flowJson.edges.length;
        const flowName = flowJson.name || 'Flow';
        return `✅ **Flow "${flowName}" loaded!**\n\nAdded **${nodeCount} nodes** and **${edgeCount} connections** to the canvas.`;
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
