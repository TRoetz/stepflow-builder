import { describe, it, expect, beforeEach } from 'vitest';
import { useAiAssistantStore } from '@stores/useAiAssistantStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import { schemaById } from '@schemas/index';

describe('AI Assistant Store', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [], selectedNodeId: null });
    useEdgeStore.setState({ edges: [] });
    useAiAssistantStore.setState({
      messages: [],
      isOpen: false,
      isCollapsed: false,
      isTyping: false,
    });
    useAiModelConfigStore.setState({
      provider: 'ollama',
      baseUrl: 'http://localhost:11434',
      apiKey: '',
      defaultModel: 'llama3',
      temperature: 0.7,
      maxTokens: 2048,
      topP: 1,
    });
  });

  it('should generate flow and render nodes and edges onto canvas when AI outputs standard AiFlowJson', async () => {
    const jsonOutput = `
\`\`\`json
{
  "name": "Order Process Flow",
  "nodes": [
    { "schemaId": "stepflow:terminal:start", "label": "START" },
    { "schemaId": "stepflow:api:http", "label": "Fetch Order", "config": { "url": "https://api.example.com/order" } },
    { "schemaId": "stepflow:terminal:end", "label": "END" }
  ],
  "edges": [
    { "source": "START", "target": "Fetch Order" },
    { "source": "Fetch Order", "target": "END" }
  ]
}
\`\`\`
`;

    // Mock global fetch to return the jsonOutput
    globalThis.fetch = async () => ({
      ok: true,
      json: async () => ({
        choices: [
          {
            message: {
              content: jsonOutput,
            },
          },
        ],
      }),
    }) as unknown as Response;

    const response = await useAiAssistantStore.getState().generateResponse('Build an order processing flow');

    expect(response).toContain('Flow "Order Process Flow" loaded!');
    
    // Check canvas nodes in useNodeStore
    const nodes = useNodeStore.getState().nodes;
    expect(nodes).toHaveLength(3);
    expect(nodes[0].data.schemaId).toBe('stepflow:terminal:start');
    expect(nodes[0].data.label).toBe('START');
    expect(nodes[1].data.schemaId).toBe('stepflow:api:http');
    expect(nodes[1].data.label).toBe('Fetch Order');
    expect(nodes[2].data.schemaId).toBe('stepflow:terminal:end');

    // Check canvas edges in useEdgeStore
    const edges = useEdgeStore.getState().edges;
    expect(edges).toHaveLength(2);
    expect(edges[0].source).toBe(nodes[0].id);
    expect(edges[0].target).toBe(nodes[1].id);
    expect(edges[1].source).toBe(nodes[1].id);
    expect(edges[1].target).toBe(nodes[2].id);
  });

  it('should support Amazon States Language (states object) and render onto canvas', async () => {
    const aslOutput = JSON.stringify({
      startAt: "ValidateOrder",
      states: {
        ValidateOrder: {
          type: "Task",
          resource: "http://validateorder",
          next: "CheckCredit"
        },
        CheckCredit: {
          type: "Choice",
          choices: [
            { next: "ApproveOrder" }
          ]
        },
        ApproveOrder: {
          type: "Pass",
          next: "END"
        }
      }
    });

    globalThis.fetch = async () => ({
      ok: true,
      json: async () => ({
        choices: [
          {
            message: {
              content: `\`\`\`json\n${aslOutput}\n\`\`\``,
            },
          },
        ],
      }),
    }) as unknown as Response;

    const response = await useAiAssistantStore.getState().generateResponse('Create order flow ASL');

    expect(response).toContain('loaded!');

    const nodes = useNodeStore.getState().nodes;
    expect(nodes.length).toBeGreaterThanOrEqual(4); // Prepended START + 3 states

    const edges = useEdgeStore.getState().edges;
    expect(edges.length).toBeGreaterThanOrEqual(3);
  });
  describe('focused-state context for assistant', () => {
    const sqlSchema = schemaById.get('stepflow:data:sql')!;

    beforeEach(() => {
      useNodeStore.setState({
        nodes: [
          {
            id: 'start_1',
            position: { x: 0, y: 0 },
            data: { schemaId: 'stepflow:terminal:start', label: 'START', configuration: {}, isCollapsed: false, isDisabled: false },
          },
          {
            id: 'sql_q',
            position: { x: 320, y: 0 },
            data: {
              schemaId: 'stepflow:data:sql',
              label: 'Database SQL Query',
              configuration: { query: 'SELECT * FROM orders WHERE id = @id' },
              isCollapsed: false,
              isDisabled: false,
            },
          },
        ],
        selectedNodeId: null,
      });
      useEdgeStore.setState({ edges: [{ id: 'e1', source: 'start_1', target: 'sql_q' }] });
      // Outer beforeEach does not reset context — clear any stale focus state.
      useAiAssistantStore.getState().setFocusedNode(null);
    });

    it('populates rich per-state context when a node is focused', () => {
      useAiAssistantStore.getState().focusNodeForAssistant('sql_q');

      const ctx = useAiAssistantStore.getState().context;
      expect(ctx.selectedNodeId).toBe('sql_q');
      expect(ctx.selectedNodeName).toBe('Database SQL Query');
      expect(ctx.selectedNodeSchemaId).toBe('stepflow:data:sql');
      expect(ctx.selectedNodeCategory).toBe(sqlSchema.category);
      expect(ctx.selectedNodeDescription).toBe(sqlSchema.description);
      expect(ctx.selectedNodeConfigValues).toEqual({ query: 'SELECT * FROM orders WHERE id = @id' });
      expect(ctx.selectedNodeInputs?.map((p) => p.label)).toEqual(sqlSchema.inputs.map((p) => p.label));
      expect(ctx.selectedNodeOutputs?.map((p) => p.label)).toEqual(sqlSchema.outputs.map((p) => p.label));
      // Whole-node token of the upstream START state is in scope.
      expect(ctx.availableVariables).toContain('{{start}}');

      // Panel opens with a system hint naming the focused state.
      const s = useAiAssistantStore.getState();
      expect(s.isOpen).toBe(true);
      expect(s.messages.some((m) => m.role === 'system' && m.content.includes('Database SQL Query'))).toBe(true);
    });

    it('injects the focused state into the system prompt sent to the model', async () => {
      useAiAssistantStore.getState().setFocusedNode('sql_q');

      let capturedSystem = '';
      globalThis.fetch = (async (_url: unknown, init?: { body?: string }) => {
        const body = JSON.parse(init?.body ?? '{}') as { messages?: Array<{ role: string; content: string }> };
        capturedSystem = body.messages?.find((m) => m.role === 'system')?.content ?? '';
        return {
          ok: true,
          json: async () => ({ choices: [{ message: { content: 'Use the query field to set your SQL.' } }] }),
        } as unknown as Response;
      }) as unknown as typeof fetch;

      await useAiAssistantStore.getState().generateResponse('How do I use this state?');

      expect(capturedSystem).toContain('## FOCUSED STATE');
      expect(capturedSystem).toContain('Database SQL Query');
      expect(capturedSystem).toContain('stepflow:data:sql');
      expect(capturedSystem).toContain('SELECT * FROM orders WHERE id = @id');
      expect(capturedSystem).toContain('{{start}}');
    });

    it('clears the focused state when selection is removed', () => {
      useAiAssistantStore.getState().setFocusedNode('sql_q');
      expect(useAiAssistantStore.getState().context.selectedNodeId).toBe('sql_q');

      useAiAssistantStore.getState().setFocusedNode(null);
      const ctx = useAiAssistantStore.getState().context;
      expect(ctx.selectedNodeId).toBeNull();
      expect(ctx.selectedNodeName).toBeNull();
      expect(ctx.selectedNodeConfigValues).toBeNull();
      expect(ctx.availableVariables).toBeNull();
    });
  });
});
