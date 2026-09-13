import { useNodeStore, StepNode } from '@stores/useNodeStore';
import { useEdgeStore, StepEdge } from '@stores/useEdgeStore';
import { schemaById } from '@schemas/index';
import { flowTemplates } from '@schemas/templates';
import { useUndoRedoStore } from '@stores/useUndoRedoStore';

/**
 * Flow data format (Amazon States Language compatible).
 */
/** Per-node canvas metadata for round-trip fidelity (label + position). */
export interface CanvasNodeMeta {
  label: string;
  position: { x: number; y: number };
}

/** Optional canvas layout stored alongside the ASL so save/load preserves node names and positions. Ignored by the engine. */
export interface CanvasMetadata {
  nodes: Record<string, CanvasNodeMeta>;
}

export interface StateMachineDefinition {
  startAt: string;
  states: Record<string, StateDefinition>;
  /** Canvas metadata (positions + labels) keyed by state id — ignored by the engine. */
  canvas?: CanvasMetadata;
}

export interface StateDefinition {
  type: string;
  resource?: string;
  next?: string;
  end?: boolean;
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
  /** Choice state branches (ASL): either a JSONata `expression` or structured rule fields (Variable + operator). */
  choices?: Array<Record<string, unknown> & { next?: string }>;
  /** Fallback target for a Choice state. */
  default?: string;
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
    return this.compileGraphToAsl(useNodeStore.getState().nodes, useEdgeStore.getState().edges);
  },

  /**
   * Compile a graph (xyflow nodes + edges) into an executable ASL definition.
   * Used for both the canvas export and Map node iterator bodies saved in graph format.
   */
  compileGraphToAsl(nodes: StepNode[], edges: StepEdge[]): StateMachineDefinition {

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
          iterator: this.normalizeIteratorDefinition(subFlow?.definition),
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
      } else if (aslType === 'FormCapture') {
        const config = (node.data?.configuration || {}) as Record<string, unknown>;
        states[node.id] = {
          type: 'FormCapture',
          next: nextNodes[0] || undefined,
          task: {
            formId: config.formId ?? '',
            title: config.title ?? '',
            assignee: config.assignee ?? '',
          },
          completion: { Type: 'form' },
          resultPath: (config.resultPath as string) || undefined,
          comment: node.data?.description || undefined,
        };
      } else if (aslType === 'Choice') {
        const config = (node.data?.configuration || {}) as Record<string, unknown>;
        // Branch edges by source handle; fall back to positional order (first edge = true branch).
        const trueEdge = connectedEdges.find((e) => e.sourceHandle === 'output_true') ?? connectedEdges[0];
        const falseEdge = connectedEdges.find((e) => e.sourceHandle === 'output_false') ?? connectedEdges[1];
        states[node.id] = {
          type: 'Choice',
          choices: [{ expression: String(config.condition || ''), next: trueEdge?.target }],
          default: falseEdge?.target,
          comment: node.data?.description || undefined,
          inputs: schema?.inputs.map((i) => i.id),
          outputs: schema?.outputs.map((o) => o.id),
        };
      } else {
        const config = (node.data?.configuration || {}) as Record<string, unknown>;
        const schemaId = node.data?.schemaId as string;
        let parameters: Record<string, unknown> | undefined;

        if (schemaId.startsWith('stepflow:data:exchange')) {
          // No Parameters at all: the executor ingests the upstream output directly.
          parameters = undefined;
        } else if (schemaId.startsWith('stepflow:api:http')) {
          // Structured contract mirroring the C# http handler exactly.
          const method = String(config.method || 'GET').toUpperCase();
          parameters = { __handler: 'http', method };
          if (method !== 'GET' && method !== 'DELETE') {
            let body = config.body;
            if (typeof body === 'string' && body.trim()) {
              try {
                body = JSON.parse(body);
              } catch {
                /* keep raw string */
              }
            }
            parameters.body = body ?? {};
          }
          const headers = safeParseRecord(config.headers);
          if (headers) parameters.headers = headers;
          if (config.authentication === 'bearer' && config.authToken) {
            parameters.auth = { type: 'Bearer', token: String(config.authToken) };
          }
          if (config.includeStatus === true) parameters.includeStatus = true;
        } else if (schemaId.startsWith('stepflow:ssh:')) {
          // Static command wins; otherwise pass through the upstream input text.
          const hasStaticCommand = typeof config.command === 'string' && config.command.trim().length > 0;
          const incomingCount = edges.filter((e) => e.target === node.id).length;
          parameters = { ...config };
          if (!hasStaticCommand && incomingCount > 0) {
            delete parameters.command; // avoid an empty literal shadowing the passthrough
            parameters['command.$'] = '$';
          }
        } else if (schemaId.startsWith('stepflow:transform:jsonata')) {
          // The engine evaluates the expression against input_data; pass through the upstream output.
          parameters = { ...config, 'input_data.$': '$' };
        } else if (schemaId.startsWith('stepflow:data:eav')) {
          // The engine's eav:// handler persists input.values ?? the input minus control keys; for write/update/patch, copy the upstream output into values.
          const op = String(config.operation || 'read');
          parameters = op === 'write' || op === 'update' || op === 'patch'
            ? { ...config, 'values.$': '$' }
            : { ...config };
        } else {
          parameters = { ...config };
        }

        const isEnd = schemaId === 'stepflow:terminal:end';
        states[node.id] = {
          type: aslType,
          ...(isEnd ? {} : { resource: buildResourceUri(node) }),
          next: nextNodes[0] || undefined,
          ...(parameters && !isEnd ? { parameters } : {}),
          ...(isEnd ? { end: true } : {}),
          comment: node.data?.description || undefined,
          inputs: schema?.inputs.map((i) => i.id),
          outputs: schema?.outputs.map((o) => o.id),
        };
      }
    }

    const canvasNodes: Record<string, CanvasNodeMeta> = {};
    for (const node of nodes) {
      canvasNodes[node.id] = {
        label: String(node.data?.label ?? node.id),
        position: { x: node.position.x, y: node.position.y },
      };
    }

    return {
      startAt: startNode?.id || '',
      states,
      canvas: { nodes: canvasNodes },
    };
  },

  /**
   * Normalize a saved iterator definition into compiled ASL. The engine's Map executor requires an executable sub-flow ({startAt, states}); bodies created by ensureIteratorBody are stored in graph format ({nodes, edges}), so compile them here. Already-compiled ASL passes through as-is; anything else falls back to a placeholder.
   */
  normalizeIteratorDefinition(definition: unknown): StateMachineDefinition {
    if (definition && typeof definition === 'object') {
      const d = definition as Record<string, unknown>;
      if (Array.isArray(d.nodes) && Array.isArray(d.edges)) {
        // Graph format — compile to ASL. Edges reference nodes by label, so node ids become labels.
        const subEdges: StepEdge[] = (d.edges as Array<{ source?: string; target?: string }>).map((e) => ({
          id: `edge-${e.source}-${e.target}`,
          source: String(e.source ?? ''),
          target: String(e.target ?? ''),
        }));
        return this.compileGraphToAsl(graphNodesToStepNodes(d.nodes), subEdges);
      }
      if (typeof d.startAt === 'string' && d.states && typeof d.states === 'object') {
        // Already compiled ASL — pass through.
        return definition as StateMachineDefinition;
      }
    }
    return {
      startAt: 'PassThrough',
      states: { PassThrough: { type: 'Pass', comment: 'Placeholder iterator flow. Please select a valid target flow.' } },
    };
  },

  /**
   * Import a StateMachineDefinition to the canvas.
   * Returns true when the definition carried full canvas layout (positions + labels restored verbatim), so callers can skip auto-layout.
   */
  importFlow(definition: unknown, offset: { x: number; y: number } = { x: 100, y: 100 }): boolean {
    // Snapshot the canvas being replaced: importing over someone's work
    // must stay recoverable via undo.
    useUndoRedoStore.getState().pushSnapshot();
    // Clear existing
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });

    if (!definition || typeof definition !== 'object') return false;

    const data = definition as Record<string, unknown>;

    // Case 1: Standard AiFlowJson format ({ nodes: [...], edges: [...] })
    if (Array.isArray(data.nodes) && Array.isArray(data.edges)) {
      const addNode = useNodeStore.getState().addNode;
      const updateNodeData = useNodeStore.getState().updateNodeData;
      const addEdge = useEdgeStore.getState().addEdge;

      let startY = offset.y;
      const labelToId = new Map<string, string>();
      const labelToSchema = new Map<string, string>();

      for (const nodeDef of data.nodes as Array<{ schemaId: string; label: string; config?: Record<string, unknown>; position?: { x: number; y: number } }>) {
        const position = nodeDef.position ?? { x: offset.x, y: startY };
        addNode(nodeDef.schemaId, position);

        const newNodes = useNodeStore.getState().nodes;
        const newNode = newNodes[newNodes.length - 1];
        if (newNode) {
          labelToId.set(nodeDef.label, newNode.id);
          labelToSchema.set(nodeDef.label, nodeDef.schemaId);

          const updates: Record<string, unknown> = { label: nodeDef.label };
          if (nodeDef.config) {
            updates.configuration = { ...newNode.data.configuration, ...nodeDef.config };
          }
          updateNodeData(newNode.id, updates);
        }

        startY += 150;
      }

      // Choice nodes branch by handle: first outgoing edge = true, second = false (template edges are ordered).
      const choiceEdgeIndex = new Map<string, number>();
      for (const edgeDef of data.edges as Array<{ source: string; target: string }>) {
        const sourceId = labelToId.get(edgeDef.source);
        const targetId = labelToId.get(edgeDef.target);

        if (sourceId && targetId) {
          let sourceHandle: string | undefined;
          if (labelToSchema.get(edgeDef.source) === 'stepflow:flow:choice') {
            const idx = choiceEdgeIndex.get(sourceId) ?? 0;
            choiceEdgeIndex.set(sourceId, idx + 1);
            sourceHandle = idx === 0 ? 'output_true' : 'output_false';
          }
          addEdge({
            id: `edge-${sourceId}-${targetId}`,
            source: sourceId,
            target: targetId,
            type: 'step-edge',
            ...(sourceHandle ? { sourceHandle } : {}),
          });
        }
      }
      return (data.nodes as Array<{ position?: unknown }>).every((n) => n.position != null);
    }

    // Case 2: StateMachineDefinition / ASL format ({ startAt: "...", states: { ... } })
    const states = (data.states || {}) as Record<string, StateDefinition>;
    const canvasNodes = data.canvas && typeof data.canvas === 'object' ? ((data.canvas as CanvasMetadata).nodes ?? undefined) : undefined;
    let y = offset.y;
    const nodeIds = Object.keys(states);
    const stateKeyToNodeId = new Map<string, string>();

    for (const nodeId of nodeIds) {
      const state = states[nodeId];
      const schemaId = resolveSchemaId(state);

      // Add node (saved canvas position when available, else stack at the offset)
      const meta = canvasNodes?.[nodeId];
      useNodeStore.getState().addNode(schemaId, meta ? meta.position : { x: offset.x, y });

      // Update node data
      const currentNodes = useNodeStore.getState().nodes;
      const newNode = currentNodes[currentNodes.length - 1];
      if (newNode) {
        stateKeyToNodeId.set(nodeId, newNode.id);
        
        useNodeStore.getState().updateNodeData(newNode.id, {
          label: meta?.label ?? nodeId, // Saved canvas label when available, else the ASL state key (e.g. "ValidateOrder")
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

        // 2. Handle Choice state choices (+ default fallback) with branch handles
        if (Array.isArray(state.choices)) {
          let choiceIdx = 0;
          for (const choice of state.choices) {
            const next = typeof choice.next === 'string' ? choice.next : undefined;
            if (next) {
              const targetUuid = stateKeyToNodeId.get(next);
              if (targetUuid) {
                addEdge({
                  id: `edge-${sourceUuid}-${targetUuid}`,
                  source: sourceUuid,
                  target: targetUuid,
                  type: 'step-edge',
                  ...(choiceIdx === 0 ? { sourceHandle: 'output_true' } : {}),
                });
              }
            }
            choiceIdx++;
          }
        }
        if (typeof state.default === 'string') {
          const targetUuid = stateKeyToNodeId.get(state.default);
          if (targetUuid) {
            addEdge({
              id: `edge-${sourceUuid}-${targetUuid}`,
              source: sourceUuid,
              target: targetUuid,
              type: 'step-edge',
              sourceHandle: 'output_false',
            });
          }
        }
      }
    }

    // Full layout restored only when every state had canvas metadata.
    return canvasNodes ? nodeIds.every((id) => Boolean(canvasNodes[id])) : false;
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
  if (schemaId?.startsWith('stepflow:data:exchange')) return `dataexchange://${config?.profileId || 'default'}`;
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
  if (schemaId?.startsWith('stepflow:ssh:')) return `ssh://${config?.host || 'default'}`;
  if (schemaId?.startsWith('stepflow:fetch:')) return `fetch://${config?.host || 'default'}?proto=${(config?.protocol as string) || 'scp'}`;
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
  if (schemaId === 'stepflow:formcapture:capture') return 'FormCapture';

  // Default standard categories: api, transform, rule, data, ai are all "Task" in ASL
  if (['api', 'transform', 'rule', 'data', 'ai'].includes(category)) return 'Task';

  return 'Task';
}
function resolveSchemaId(state: StateDefinition): string {
  // Terminal end states are exported as Pass + end:true (the engine has no End state type).
  if (state.end === true) return 'stepflow:terminal:end';
  // Map resource URI back to schemaId
  const resource = state.resource || '';
  if (state.type === 'HumanTask' || resource.startsWith('human://')) return 'stepflow:human:task';
  if (state.type === 'FormCapture' || resource.startsWith('form://')) return 'stepflow:formcapture:capture';
  if (state.type === 'Choice') return 'stepflow:flow:choice';
  if (resource.startsWith('ai://')) return 'stepflow:ai:decision';
  if (resource.startsWith('rule://')) return 'stepflow:rule:rule_engine';
  if (resource.startsWith('sql://')) return 'stepflow:data:sql';
  if (resource.startsWith('duckdb://')) return 'stepflow:data:duckdb';
  if (resource.startsWith('eav://')) return 'stepflow:data:eav';
  if (resource.startsWith('dataexchange://')) return 'stepflow:data:exchange';
  if (resource.startsWith('http://') || resource.startsWith('https://')) return 'stepflow:api:http';
  if (resource.startsWith('api://')) return 'stepflow:api:registered';
  if (resource.startsWith('ssh://')) return 'stepflow:ssh:command';
  if (resource.startsWith('fetch://')) return 'stepflow:fetch:files';
  if (resource.startsWith('transform://jsonata')) return 'stepflow:transform:jsonata';
  if (resource.startsWith('transform://')) return 'stepflow:transform:script';
  if (resource.startsWith('flow://')) return 'stepflow:subflow:invoke';
  if (resource.startsWith('utility://pass')) return 'stepflow:utility:pass';
  if (resource.startsWith('utility://wait')) return 'stepflow:utility:wait';
  if (resource.startsWith('utility://branch')) return 'stepflow:utility:branch';

  return 'stepflow:utility:pass';
}

/** Parse a JSON string (or pass through an object) into a flat record; null when absent or invalid. */
function safeParseRecord(value: unknown): Record<string, unknown> | null {
  if (value == null) return null;
  let v = value;
  if (typeof v === 'string') {
    try {
      v = JSON.parse(v);
    } catch {
      return null;
    }
  }
  return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : null;
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
  if (state.type === 'FormCapture') {
    const task = (state.task || {}) as Record<string, unknown>;
    return {
      formId: task.formId ?? '',
      title: task.title ?? '',
      assignee: task.assignee ?? '',
      resultPath: state.resultPath ?? '',
    };
  }
  // Choice — restore the condition expression; synthesize JSONata for structured ASL rules.
  if (state.type === 'Choice') {
    const first = state.choices?.[0];
    let condition = '';
    if (first) {
      if (typeof first.expression === 'string' && first.expression.trim()) {
        condition = first.expression;
      } else {
        const { next: _next, ...ruleFields } = first as Record<string, unknown>;
        condition = choiceRuleToJsonata(ruleFields) ?? '';
      }
    }
    return { condition };
  }
  // Data Exchange — profile id lives in the resource URI.
  if ((state.resource || '').startsWith('dataexchange://')) {
    const profileId = (state.resource as string).slice('dataexchange://'.length).replace(/\/+$/, '');
    return { profileId };
  }
  // HTTP request — structured __handler contract back into node config fields.
  if ((state.parameters as Record<string, unknown> | undefined)?.__handler === 'http') {
    const p = state.parameters as Record<string, unknown>;
    let body = p.body;
    if (body !== undefined && typeof body !== 'string') body = JSON.stringify(body, null, 2);
    const headers = safeParseRecord(p.headers);
    const auth = p.auth as Record<string, unknown> | undefined;
    return {
      url: state.resource || '',
      method: String(p.method ?? 'GET'),
      ...(body !== undefined ? { body } : {}),
      ...(headers ? { headers: JSON.stringify(headers, null, 2) } : {}),
      ...(auth?.token ? { authentication: auth.type === 'Basic' ? 'basic' : 'bearer', authToken: String(auth.token) } : {}),
      ...(p.includeStatus === true ? { includeStatus: true } : {}),
    };
  }
  const params: Record<string, unknown> = { ...(state.parameters || {}) };
  // Template-resolution markers (e.g. "input_data.$") are engine-side wiring, not node config.
  for (const key of Object.keys(params)) if (key.endsWith('.$')) delete params[key];
  // Resource-URI-encoded config: MCP-created flows carry it only in the URI — restore what the UI expects as node fields.
  const resource = state.resource || '';
  if (resource.startsWith('sql://') && !params.connectionString)
    params.connectionString = resource.slice('sql://'.length).replace(/\/+$/, '');
  else if (resource.startsWith('eav://') && !params.entityType)
    params.entityType = resource.slice('eav://'.length).replace(/\/+$/, '');
  else if (resource.startsWith('ai://') && !params.llmService)
    params.llmService = resource.slice('ai://'.length).replace(/\/+$/, '');
  else if (resource.startsWith('rule://') && !params.evaluationMode)
    params.evaluationMode = resource.slice('rule://'.length).replace(/\/+$/, '');
  return params;
}

/** Synthesize an equivalent JSONata expression for a structured ASL choice rule (best-effort; null when the operator is unsupported). */
function choiceRuleToJsonata(rule: Record<string, unknown>): string | null {
  if (Array.isArray(rule.And)) {
    const parts = rule.And.map((r) => choiceRuleToJsonata(r as Record<string, unknown>)).filter(Boolean);
    return parts.length ? parts.map((p) => `(${p})`).join(' and ') : null;
  }
  if (Array.isArray(rule.Or)) {
    const parts = rule.Or.map((r) => choiceRuleToJsonata(r as Record<string, unknown>)).filter(Boolean);
    return parts.length ? parts.map((p) => `(${p})`).join(' or ') : null;
  }
  if (rule.Not && typeof rule.Not === 'object') {
    const inner = choiceRuleToJsonata(rule.Not as Record<string, unknown>);
    return inner ? `not (${inner})` : null;
  }

  const v = typeof rule.Variable === 'string' ? rule.Variable : '';
  if (!v) return null;
  switch (true) {
    case rule.StringEquals != null: return `${v} = ${JSON.stringify(String(rule.StringEquals))}`;
    case typeof rule.NumericEquals === 'number': return `${v} = ${rule.NumericEquals}`;
    case typeof rule.BooleanEquals === 'boolean': return `${v} = ${rule.BooleanEquals}`;
    case typeof rule.NumericGreaterThan === 'number': return `${v} > ${rule.NumericGreaterThan}`;
    case typeof rule.NumericGreaterThanEquals === 'number': return `${v} >= ${rule.NumericGreaterThanEquals}`;
    case typeof rule.NumericLessThan === 'number': return `${v} < ${rule.NumericLessThan}`;
    case typeof rule.NumericLessThanEquals === 'number': return `${v} <= ${rule.NumericLessThanEquals}`;
    case rule.IsPresent != null: return rule.IsPresent ? `exists(${v})` : `not exists(${v})`;
    case rule.IsNull != null: return rule.IsNull ? `${v} = $null` : `${v} != $null`;
    case rule.IsString != null: return `${rule.IsString ? '' : 'not '}type(${v}) = 'string'`;
    case rule.IsNumeric != null: return `${rule.IsNumeric ? '' : 'not '}type(${v}) = 'number'`;
    case rule.IsBoolean != null: return `${rule.IsBoolean ? '' : 'not '}type(${v}) = 'boolean'`;
    default: return null; // timestamps, StringMatches regex, etc. — no JSONata equivalent generated
  }
}

function loadSavedFlows(): Array<{ id: string; name: string; description?: string; createdAt: string; definition: StateMachineDefinition }> {
  try {
    const saved = localStorage.getItem('stepflow-flows');
    return saved ? JSON.parse(saved) : [];
  } catch {
    return [];
  }
}

/** Convert saved graph-format nodes ({schemaId, label, config}) into StepNode objects keyed by label (graph edges reference labels). */
function graphNodesToStepNodes(raw: unknown[]): StepNode[] {
  return raw.map((n) => {
    const nodeDef = n as { schemaId?: string; label?: string; description?: string; config?: Record<string, unknown>; position?: { x: number; y: number } };
    return {
      id: nodeDef.label || `node-${Math.random().toString(36).slice(2)}`,
      data: { schemaId: nodeDef.schemaId ?? '', label: nodeDef.label ?? '', description: nodeDef.description, configuration: nodeDef.config ?? {} },
      position: nodeDef.position ?? { x: 0, y: 0 },
    } as StepNode;
  });
}
