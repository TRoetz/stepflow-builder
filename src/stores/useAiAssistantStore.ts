import { create } from 'zustand';
import { categoryPromptTemplates } from '@stores/aiAssistantPrompts';

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

  // AI response simulation
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

// ── Context-aware response generation ──

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

const nodeHelpMap: Record<string, string> = {
  'START': 'The START node marks the entry point of your workflow. Add a description to document what this flow does. Next, connect it to your first processing state.',
  'END': 'The END node marks where your flow completes. Connect it from the last state in your execution path. You can have multiple END nodes for different branches.',
  'Choice': 'The Choice state branches execution based on a condition. Configure the condition expression to evaluate your data. It has True and False outputs for different paths.',
  'Map': 'The Map state iterates over arrays. Configure the iterator variable name and connect a sub-flow to process each item. Results are collected into an array output.',
  'Parallel': 'The Parallel state runs branches concurrently. Set the number of branches and connect independent sub-flows. All branches must complete before continuing.',
  'Succeed': 'The Succeed state marks a successful endpoint. Use it to terminate a branch with a success status. Configure a success message for documentation.',
  'Fail': 'The Fail state marks an error endpoint. Use it when a condition indicates failure. Configure an error message and error code.',
  'AI Decision': 'The AI Decision state sends data to an AI engine. Configure the Resource URI (e.g., ai://classify) and the input data expression. The output contains the AI\'s decision.',
  'AI Text Generation': 'The AI Text Generation state uses an LLM to generate text. Configure the prompt template, model parameters like temperature and max tokens.',
  'Rule Engine': 'The Rule Engine state evaluates business rules. Configure the rule set as JSON with name/expression/outcome fields, evaluation mode, and default outcome.',
  'MS RulesEngine': 'The MS RulesEngine state evaluates rules using Microsoft RulesEngine. Configure rule definitions with C# lambda expressions (input => condition).',
  'SQL Query': 'The SQL Query state executes database queries. Configure the connection, SQL statement, and parameter bindings.',
  'DuckDB Query': 'The DuckDB Query state runs SQL queries for in-memory analytics. Configure the SQL statement and data sources.',
  'EAV Operation': 'The EAV Operation state manages Entity-Attribute-Value data. Configure the entity, attributes, and operation type.',
  'HTTP Request': 'The HTTP Request state calls external APIs. Configure the method, URL, headers, body template, and timeout.',
  'Registered API': 'The Registered API state uses pre-registered integrations. Configure the API reference and endpoint.',
  'JSONata Processor': 'The JSONata Processor state transforms data using JSONata expressions. Configure the expression and input/output paths.',
  'Script Execution': 'The Script Execution state runs custom code. Choose the engine (JavaScript/Python) and write the script body.',
  'Pass Through': 'The Pass Through state forwards data unchanged. Useful for labeling, routing, or debugging data flow.',
  'Wait': 'The Wait state pauses execution. Configure the wait type (duration or timestamp) and the wait value.',
  'Branch': 'The Branch state routes data to multiple paths. Configure branch conditions for each output.',
  'Sub-Flow Call': 'The Sub-Flow Call state invokes another workflow. Reference the target flow and configure input mapping.',
};

function getContextualHelp(ctx: WorkflowContext, userMessage: string): string {
  const lower = userMessage.toLowerCase();
  const queriedNode = Object.keys(nodeHelpMap).find(
    (name) => lower.includes(name.toLowerCase())
  );
  if (queriedNode) {
    return nodeHelpMap[queriedNode];
  }

  // Check if asking about the recently added node
  if (ctx.recentlyAddedNodeName && lower.includes('it')) {
    return nodeHelpMap[ctx.recentlyAddedNodeName] || `I can help configure the ${ctx.recentlyAddedNodeName} state. What specific aspect would you like help with?`;
  }

  // Check if asking about the selected node
  if (ctx.selectedNodeName && lower.includes('selected')) {
    return nodeHelpMap[ctx.selectedNodeName] || `The ${ctx.selectedNodeName} state is currently selected. You can configure its properties in the right panel.`;
  }

  // Generic workflow advice
  if (!ctx.hasStartNode && ctx.nodeCount > 0) {
    return 'I notice your flow doesn\'t have a START node. Every workflow should begin with a START state from the Terminal category. Would you like help setting up the flow structure?';
  }

  if (ctx.hasStartNode && !ctx.hasEndNode && ctx.nodeCount > 1) {
    return 'Your flow has a START node but no END node. Consider adding an END state from the Terminal category to mark where the flow completes.';
  }

  return 'I can help you with state configuration, control flow design, variable expressions, and API connections. What would you like to know?';
}

export const useAiAssistantStore = create<AiAssistantState>((set, get) => ({
  messages: [
    {
      id: 'welcome',
      role: 'assistant',
      content: 'Welcome to StepFlow Builder! I\'m your AI assistant. I can help you configure states, design control flow, set up variables, and connect APIs. Drag states from the palette and I\'ll provide guidance as you build.',
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
          content: 'Chat cleared. How can I help with your workflow?',
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

    // Auto-suggest help for the new node
    const helpText = nodeHelpMap[nodeName]
      ? `You just added a **${nodeName}** state (${category}). ${nodeHelpMap[nodeName]}`
      : `You just added a **${nodeName}** state from the ${category} category. Click on it to configure its properties.`;

    set((state) => ({
      messages: [
        ...state.messages,
        {
          id: `suggestion-${Date.now()}`,
          role: 'assistant',
          content: helpText,
          timestamp: Date.now(),
        },
      ],
    }));
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

    // Simulate AI processing delay
    await new Promise((r) => setTimeout(r, 400 + Math.random() * 600));

    const ctx = get().context;
    let systemPrompt = '';

    // Inject category-specific prompt if a node is selected
    if (ctx.selectedNodeCategory && ctx.selectedNodeConfigFields) {
      const template = categoryPromptTemplates[ctx.selectedNodeCategory as keyof typeof categoryPromptTemplates];
      if (template) {
        systemPrompt = template(ctx.selectedNodeConfigFields) + '\n\n';
      }
    }

    const contextualHelp = getContextualHelp(ctx, userMessage);
    const contextSummary = buildContextSummary(ctx);

    const response = `${systemPrompt}${contextualHelp}\n\n*Current workflow: ${contextSummary}*`;

    set({ isTyping: false });
    return response;
  },
}));
