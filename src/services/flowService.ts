import { useNodeStore, StepNode } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { schemaById } from '@schemas/index';

/**
 * Flow data format (Amazon States Language compatible).
 */
export interface StateMachineDefinition {
  startAt: string;
  states: Record<string, StateDefinition>;
}

export interface StateDefinition {
  type: string;
  resource: string;
  next?: string;
  parameters?: Record<string, unknown>;
  comment?: string;
  inputs?: string[];
  outputs?: string[];
}

/**
 * Service for flow CRUD operations.
 * Converts between xyflow format and Amazon States Language JSON.
 */
export const FlowService = {
  /**
   * Export current canvas as a StateMachineDefinition.
   */
  exportFlow(): StateMachineDefinition {
    const nodes = useNodeStore.getState().nodes;
    const edges = useEdgeStore.getState().edges;

    // Find start node (first node or node with no incoming edges)
    const targetNodeIds = new Set(edges.map((e) => e.target));
    const startNode = nodes.find((n) => !targetNodeIds.has(n.id)) || nodes[0];

    // Build states
    const states: Record<string, StateDefinition> = {};

    for (const node of nodes) {
      const schema = schemaById.get(node.data?.schemaId as string);
      const connectedEdges = edges.filter((e) => e.source === node.id);
      const nextNodes = connectedEdges.map((e) => {
        const target = nodes.find((n) => n.id === e.target);
        return target?.id;
      }).filter(Boolean);

      states[node.id] = {
        type: schema?.category || 'utility',
        resource: buildResourceUri(node),
        next: nextNodes[0] || undefined,
        parameters: node.data?.configuration || {},
        comment: node.data?.description || undefined,
        inputs: schema?.inputs.map((i) => i.id),
        outputs: schema?.outputs.map((o) => o.id),
      };
    }

    return {
      startAt: startNode?.id || '',
      states,
    };
  },

  /**
   * Import a StateMachineDefinition to the canvas.
   */
  importFlow(definition: StateMachineDefinition, offset: { x: number; y: number } = { x: 100, y: 100 }): void {
    // Clear existing
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });

    let y = offset.y;
    const nodeIds = Object.keys(definition.states);

    for (const nodeId of nodeIds) {
      const state = definition.states[nodeId];
      const schemaId = resolveSchemaId(state);

      // Add node
      useNodeStore.getState().addNode(schemaId, {
        x: offset.x,
        y: y,
      });

      // Update node data
      const currentNodes = useNodeStore.getState().nodes;
      const newNode = currentNodes[currentNodes.length - 1];
      if (newNode) {
        useNodeStore.getState().updateNodeData(newNode.id, {
          configuration: state.parameters || {},
          description: state.comment,
        });
      }

      y += 150;
    }
  },

  /**
   * Save flow to localStorage (placeholder for backend API).
   */
  async saveFlow(name: string, description?: string): Promise<void> {
    const flowDef = FlowService.exportFlow();
    const flows = loadSavedFlows();
    flows.push({
      id: `flow-${Date.now()}`,
      name,
      description,
      createdAt: new Date().toISOString(),
      definition: flowDef,
    });
    localStorage.setItem('stepflow-flows', JSON.stringify(flows));
  },

  /**
   * Load all saved flows.
   */
  async listFlows(): Promise<Array<{ id: string; name: string; description?: string; createdAt: string }>> {
    const flows = loadSavedFlows();
    return flows.map((f) => ({
      id: f.id,
      name: f.name,
      description: f.description,
      createdAt: f.createdAt,
    }));
  },

  /**
   * Load a specific flow by ID.
   */
  async loadFlow(id: string): Promise<StateMachineDefinition | null> {
    const flows = loadSavedFlows();
    const flow = flows.find((f) => f.id === id);
    return flow?.definition || null;
  },
};

// ── Helpers ──

function buildResourceUri(node: StepNode): string {
  const schemaId = node.data?.schemaId as string;
  const config = node.data?.configuration as Record<string, unknown> | undefined;

  // Map schema to resource URI
  if (schemaId?.startsWith('stepflow:ai:')) return `ai://${config?.llmService || 'azureOpenAI'}`;
  if (schemaId?.startsWith('stepflow:rule:')) return `rule://${config?.evaluationMode || 'first_match'}`;
  if (schemaId?.startsWith('stepflow:data:sql')) return `sql://${config?.connectionString || 'default'}`;
  if (schemaId?.startsWith('stepflow:data:duckdb')) return `duckdb://default`;
  if (schemaId?.startsWith('stepflow:data:eav')) return `eav://${config?.entityType || 'default'}`;
  if (schemaId?.startsWith('stepflow:api:http')) return `http://${config?.url || 'localhost'}`;
  if (schemaId?.startsWith('stepflow:api:registered')) return `api://${config?.apiId || 'default'}`;
  if (schemaId?.startsWith('stepflow:transform:jsonata')) return `transform://jsonata`;
  if (schemaId?.startsWith('stepflow:transform:script')) return `transform://${config?.language || 'javascript'}`;
  if (schemaId?.startsWith('stepflow:utility:')) return `utility://${schemaId.split(':').pop()}`;
  if (schemaId?.startsWith('stepflow:subflow:')) return `flow://${config?.targetFlowId || 'default'}`;

  return `utility://default`;
}

function resolveSchemaId(state: StateDefinition): string {
  // Map resource URI back to schemaId
  const resource = state.resource || '';
  if (resource.startsWith('ai://')) return 'stepflow:ai:decision';
  if (resource.startsWith('rule://')) return 'stepflow:rule:rule_engine';
  if (resource.startsWith('sql://')) return 'stepflow:data:sql';
  if (resource.startsWith('duckdb://')) return 'stepflow:data:duckdb';
  if (resource.startsWith('eav://')) return 'stepflow:data:eav';
  if (resource.startsWith('http://')) return 'stepflow:api:http';
  if (resource.startsWith('api://')) return 'stepflow:api:registered';
  if (resource.startsWith('transform://jsonata')) return 'stepflow:transform:jsonata';
  if (resource.startsWith('transform://')) return 'stepflow:transform:script';
  if (resource.startsWith('flow://')) return 'stepflow:subflow:invoke';
  if (resource.startsWith('utility://pass')) return 'stepflow:utility:pass';
  if (resource.startsWith('utility://wait')) return 'stepflow:utility:wait';
  if (resource.startsWith('utility://branch')) return 'stepflow:utility:branch';

  return 'stepflow:utility:pass';
}

function loadSavedFlows(): Array<{ id: string; name: string; description?: string; createdAt: string; definition: StateMachineDefinition }> {
  try {
    const saved = localStorage.getItem('stepflow-flows');
    return saved ? JSON.parse(saved) : [];
  } catch {
    return [];
  }
}
