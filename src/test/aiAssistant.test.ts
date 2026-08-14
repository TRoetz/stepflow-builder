import { describe, it, expect, beforeEach } from 'vitest';
import { useAiAssistantStore } from '@stores/useAiAssistantStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';

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
});
