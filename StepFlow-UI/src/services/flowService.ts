import { useNodeStore, StepNode } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { schemaById } from '@schemas/index';
import { flowTemplates } from '@schemas/templates';

/**
 * Flow data format (Amazon States Language compatible).
 */
export interface StateMachineDefinition {
  startAt: string;
  states: Record<string, StateDefinition>;
}

export interface StateDefinition {
  type: string;
  resource?: string;
  next?: string;
  parameters?: Record<string, unknown>;
  task?: Record<string, unknown>;
  completion?: Record<string, unknown>;
  comment?: string;
  inputs?: string[];
  outputs?: string[];
  itemsPath?: string;
  maxConcurrency?: number;
  resultPath?: string;
  iterator?: StateMachineDefinition;
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

      const aslType = mapSchemaIdToAslStateType(node.data?.schemaId as string, schema?.category || '');
      if (aslType === 'Map') {
        const config = (node.data?.configuration || {}) as Record<string, unknown>;
        const targetFlowId = (config.targetFlowId as string) || '';

        // Find the referenced sub-flow
        const flows = loadSavedFlows();
        const subFlow = flows.find((f) => f.id === targetFlowId);

        states[node.id] = {
          type: 'Map',
          itemsPath: (config.itemsPath as string) || '$.items',
          maxConcurrency: Number(config.maxConcurrency ?? 1),
          resultPath: (config.resultPath as string) || '$.results',
          iterator: subFlow?.definition || {
            startAt: 'PassThrough',
            states: {
              'PassThrough': {
                type: 'Pass',
                comment: 'Placeholder iterator flow. Please select a valid target flow.',
              }
            }
          },
          next: nextNodes[0] || undefined
        };
      } else if (aslType === 'HumanTask') {
        const config = (node.data?.configuration || {}) as Record<string, unknown>;
        states[node.id] = {
          type: 'HumanTask',
          next: nextNodes[0] || undefined,
          task: {
            title: config.taskTitle ?? '',
            assignee: config.assignee ?? '',
          },
          completion: {
            Type: (config.completionMethod as string) || 'api',
            ...(config.completionMethod === 'file' && config.watchDirectory ? { Directory: config.watchDirectory } : {}),
          },
          resultPath: (config.resultPath as string) || undefined,
          comment: node.data?.description || undefined,
        };
      } else {
        states[node.id] = {
          type: aslType,
          resource: buildResourceUri(node),
          next: nextNodes[0] || undefined,
          parameters: node.data?.configuration || {},
          comment: node.data?.description || undefined,
          inputs: schema?.inputs.map((i) => i.id),
          outputs: schema?.outputs.map((o) => o.id),
        };
      }
    }

    return {
      startAt: startNode?.id || '',
      states,
    };
  },

  /**
   * Import a StateMachineDefinition to the canvas.
   */
  importFlow(definition: unknown, offset: { x: number; y: number } = { x: 100, y: 100 }): void {
    // Clear existing
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });

    if (!definition || typeof definition !== 'object') return;

    const data = definition as Record<string, unknown>;

    // Case 1: Standard AiFlowJson format ({ nodes: [...], edges: [...] })
    if (Array.isArray(data.nodes) && Array.isArray(data.edges)) {
      const addNode = useNodeStore.getState().addNode;
      const updateNodeData = useNodeStore.getState().updateNodeData;
      const addEdge = useEdgeStore.getState().addEdge;

      let startY = offset.y;
      const labelToId = new Map<string, string>();

      for (const nodeDef of data.nodes as Array<{ schemaId: string; label: string; config?: Record<string, unknown>; position?: { x: number; y: number } }>) {
        const position = nodeDef.position ?? { x: offset.x, y: startY };
        addNode(nodeDef.schemaId, position);

        const newNodes = useNodeStore.getState().nodes;
        const newNode = newNodes[newNodes.length - 1];
        if (newNode) {
          labelToId.set(nodeDef.label, newNode.id);

          const updates: Record<string, unknown> = { label: nodeDef.label };
          if (nodeDef.config) {
            updates.configuration = { ...newNode.data.configuration, ...nodeDef.config };
          }
          updateNodeData(newNode.id, updates);
        }

        startY += 150;
      }

      for (const edgeDef of data.edges as Array<{ source: string; target: string }>) {
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
      return;
    }

    // Case 2: StateMachineDefinition / ASL format ({ startAt: "...", states: { ... } })
    const states = (data.states || {}) as Record<string, StateDefinition>;
    let y = offset.y;
    const nodeIds = Object.keys(states);
    const stateKeyToNodeId = new Map<string, string>();

    for (const nodeId of nodeIds) {
      const state = states[nodeId];
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
        stateKeyToNodeId.set(nodeId, newNode.id);
        
        useNodeStore.getState().updateNodeData(newNode.id, {
          label: nodeId, // Set label to match the ASL state key (e.g. "ValidateOrder")
          configuration: reconstructConfiguration(state),
          description: state.comment,
        });
      }

      y += 150;
    }

    // Create edges for ASL states
    const addEdge = useEdgeStore.getState().addEdge;
    for (const nodeId of nodeIds) {
      const state = states[nodeId];
      const sourceUuid = stateKeyToNodeId.get(nodeId);

      if (sourceUuid) {
        // 1. Handle standard Next state
        if (state.next && typeof state.next === 'string') {
          const targetUuid = stateKeyToNodeId.get(state.next);
          if (targetUuid) {
            addEdge({
              id: `edge-${sourceUuid}-${targetUuid}`,
              source: sourceUuid,
              target: targetUuid,
              type: 'step-edge',
            });
          }
        }

        // 2. Handle Choice state choices
        if (Array.isArray((state as any).choices)) {
          for (const choice of (state as any).choices as any[]) {
            if (choice.next && typeof choice.next === 'string') {
              const targetUuid = stateKeyToNodeId.get(choice.next);
              if (targetUuid) {
                addEdge({
                  id: `edge-${sourceUuid}-${targetUuid}`,
                  source: sourceUuid,
                  target: targetUuid,
                  type: 'step-edge',
                });
              }
            }
          }
        }
      }
    }
  },

  /**
   * Save flow to localStorage (placeholder for backend API).
   */
  async saveFlow(name: string, description?: string): Promise<{ success: boolean; message: string }> {
    const flowDef = FlowService.exportFlow();
    const flows = loadSavedFlows();
    
    // Check if flow with same name exists, update if found, otherwise append
    const existingIndex = flows.findIndex((f) => f.name.toLowerCase() === name.trim().toLowerCase());
    if (existingIndex !== -1) {
      flows[existingIndex] = {
        ...flows[existingIndex],
        name: name.trim(),
        description: description ?? flows[existingIndex].description,
        updatedAt: new Date().toISOString(),
        definition: flowDef,
      } as unknown as typeof flows[0];
    } else {
      flows.push({
        id: `flow-${Date.now()}`,
        name: name.trim() || 'Untitled Flow',
        description,
        createdAt: new Date().toISOString(),
        definition: flowDef,
      });
    }

    try {
      localStorage.setItem('stepflow-flows', JSON.stringify(flows));
      return { success: true, message: `Flow "${name}" saved successfully!` };
    } catch (err) {
      console.error('Failed to save flow to localStorage:', err);
      return { success: false, message: 'Failed to save flow. Browser storage might be full or blocked.' };
    }
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

  /**
   * Ensure a Map node has an iterator sub-flow body.
   *
   * A Map node (`stepflow:flow:map`) executes `configuration.targetFlowId` once per
   * item in `itemsPath`. If the target flow doesn't exist (fresh scaffold, or it was
   * deleted), this creates a saved flow with a pre-wired starter body (Start → EAV row read → AI Text Gen → AI Decision) and
   * links it to the Map node's configuration and reports what happened. Idempotent:
   * an already-linked Map just gets its existing link reported.
   */
  async ensureIteratorBody(
    mapNodeId: string,
    opts?: { initialDefinition?: unknown; flowName?: string }
  ): Promise<{
    success: boolean;
    created: boolean;
    flowId?: string;
    flowName?: string;
    message: string;
  }> {
    const mapNode = useNodeStore.getState().nodes.find((n) => n.id === mapNodeId);
    if (!mapNode || (mapNode.data?.schemaId as string | undefined) !== 'stepflow:flow:map') {
      return { success: false, created: false, message: 'Select a Map node to scaffold its iterator body.' };
    }

    const flows = loadSavedFlows();
    const targetFlowId = String(mapNode.data?.configuration?.targetFlowId ?? '');
    const existing = targetFlowId ? flows.find((f) => f.id === targetFlowId) : undefined;

    if (existing) {
      return {
        success: true,
        created: false,
        flowId: existing.id,
        flowName: existing.name,
        message: `Map is already linked to "${existing.name}".`,
      };
    }

    // No (valid) link — create a starter iterator flow.
    const base = opts?.flowName ?? 'Map Iterator';
    let name = base;
    let suffix = 2;
    while (flows.some((f) => f.name.toLowerCase() === name.toLowerCase())) {
      name = `${base} (${suffix++})`;
    }

    // Default starter body (graph format — importFlow loads it verbatim, preserving
    // exact schema ids; ASL would map any ai:// resource to decision only).
    // Templates may supply a richer initial definition.
    const definition: unknown = opts?.initialDefinition ?? {
      nodes: [
        { schemaId: 'stepflow:utility:pass', label: 'Iteration Start' },
        {
          schemaId: 'stepflow:data:eav',
          label: 'Read Row',
          config: { operation: 'read', entityType: '<your-entity-type>' },
        },
        {
          schemaId: 'stepflow:ai:text',
          label: 'Generate Text',
          config: {
            systemPrompt:
              'Summarize the current source row into a concise text description. Use only fields available on $input.row.',
          },
        },
        {
          schemaId: 'stepflow:ai:decision',
          label: 'Decide',
          config: {
            prompt:
              'Evaluate the generated summary for the current row and decide whether it is approved or needs manual review. Return a single-word decision.',
          },
        },
      ],
      edges: [
        { source: 'Iteration Start', target: 'Read Row' },
        { source: 'Read Row', target: 'Generate Text' },
        { source: 'Generate Text', target: 'Decide' },
      ],
    };

    const flowId = `flow-${Date.now()}`;
    flows.push({
      id: flowId,
      name,
      description: 'Iterator body for a Map node (auto-created). Each execution receives one item row.',
      createdAt: new Date().toISOString(),
      // Saved bodies may be ASL or AiFlowJson graph format — importFlow() handles both on load.
      definition: definition as StateMachineDefinition,
    });

    try {
      localStorage.setItem('stepflow-flows', JSON.stringify(flows));
    } catch {
      flows.pop(); // restore list if persistence failed
      return { success: false, created: false, message: 'Failed to save the iterator flow — browser storage is full or blocked.' };
    }

    useNodeStore.getState().updateNodeData(mapNodeId, {
      configuration: {
        ...(mapNode.data?.configuration ?? {}),
        targetFlowId: flowId,
      },
    });

    return {
      success: true,
      created: true,
      flowId,
      flowName: name,
      message: `Created iterator flow "${name}" and linked it to the Map node.`,
    };
  },

  /**
   * Instantiate a starter template from the palette.
   * Imports the main flow (clears the canvas first) and, when the template
   * defines an iterator body, links it to the first Map node in the imported graph.
   */
  async instantiateTemplate(templateId: string): Promise<{ success: boolean; message: string }> {
    const template = flowTemplates.find((t) => t.id === templateId);
    if (!template) return { success: false, message: 'Unknown template.' };

    this.importFlow({ nodes: template.mainFlow.nodes, edges: template.mainFlow.edges });

    let note = '';
    if (template.iteratorBody) {
      const mapNode = useNodeStore
        .getState()
        .nodes.find((n) => (n.data?.schemaId as string | undefined) === 'stepflow:flow:map');
      if (mapNode) {
        const body = await this.ensureIteratorBody(mapNode.id, {
          initialDefinition: { nodes: template.iteratorBody.nodes, edges: template.iteratorBody.edges },
          flowName: `${template.name} — Iterator Body`,
        });
        note = body.success && body.created
          ? ` Its iterator body "${body.flowName}" was created and linked to the Map node.`
          : ' The existing iterator link on the Map node was kept.';
      } else {
        note = ' No Map step found in this flow — add one, then scaffold its iterator from Properties.';
      }
    }

    return { success: true, message: `Instantiated "${template.name}".${note}` };
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
  if (schemaId?.startsWith('stepflow:api:http')) {
    const url = config?.url as string || 'localhost';
    if (url.startsWith('http://') || url.startsWith('https://')) return url;
    return `http://${url}`;
  }
  if (schemaId?.startsWith('stepflow:api:registered')) return `api://${config?.apiId || 'default'}`;
  if (schemaId?.startsWith('stepflow:transform:jsonata')) return `transform://jsonata`;
  if (schemaId?.startsWith('stepflow:transform:script')) return `transform://${config?.language || 'javascript'}`;
  if (schemaId?.startsWith('stepflow:utility:')) return `utility://${schemaId.split(':').pop()}`;
  if (schemaId?.startsWith('stepflow:human:')) return `human://task`;
  if (schemaId?.startsWith('stepflow:subflow:')) return `flow://${config?.targetFlowId || 'default'}`;
  return `utility://default`;
}

function mapSchemaIdToAslStateType(schemaId: string, category: string): string {
  if (schemaId === 'stepflow:terminal:start' || schemaId === 'stepflow:terminal:end') return 'Pass';
  if (schemaId === 'stepflow:utility:pass') return 'Pass';
  if (schemaId === 'stepflow:utility:wait') return 'Wait';
  if (schemaId === 'stepflow:utility:choice' || schemaId === 'stepflow:flow:choice') return 'Choice';
  if (schemaId === 'stepflow:utility:parallel' || schemaId === 'stepflow:flow:parallel') return 'Parallel';
  if (schemaId === 'stepflow:utility:map' || schemaId === 'stepflow:flow:map') return 'Map';
  if (schemaId === 'stepflow:terminal:succeed' || schemaId === 'stepflow:flow:succeed') return 'Succeed';
  if (schemaId === 'stepflow:terminal:fail' || schemaId === 'stepflow:flow:fail') return 'Fail';
  if (schemaId === 'stepflow:human:task') return 'HumanTask';

  // Default standard categories: api, transform, rule, data, ai are all "Task" in ASL
  if (['api', 'transform', 'rule', 'data', 'ai'].includes(category)) return 'Task';

  return 'Task';
}
function resolveSchemaId(state: StateDefinition): string {
  // Map resource URI back to schemaId
  const resource = state.resource || '';
  if (state.type === 'HumanTask' || resource.startsWith('human://')) return 'stepflow:human:task';
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

/** Rebuild node configuration from an imported ASL state (handles HumanTask's Task/Completion contract). */
function reconstructConfiguration(state: StateDefinition): Record<string, unknown> {
  if (state.type === 'HumanTask') {
    const task = (state.task || {}) as Record<string, unknown>;
    const completion = (state.completion || {}) as Record<string, unknown>;
    return {
      taskTitle: task.title ?? '',
      assignee: task.assignee ?? '',
      completionMethod: String(completion.Type ?? 'api').toLowerCase(),
      watchDirectory: completion.Directory ?? '',
      resultPath: state.resultPath ?? '',
    };
  }
  return state.parameters || {};
}

function loadSavedFlows(): Array<{ id: string; name: string; description?: string; createdAt: string; definition: StateMachineDefinition }> {
  try {
    const saved = localStorage.getItem('stepflow-flows');
    return saved ? JSON.parse(saved) : [];
  } catch {
    return [];
  }
}
