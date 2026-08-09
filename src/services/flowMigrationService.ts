/**
 * Flow Migration Service
 * 
 * Imports existing flows from the Flows/ directory (Amazon States Language format)
 * and converts them to xyflow node/edge format for the canvas.
 */

import { useNodeStore, StepNode } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { schemaById } from '@schemas/index';
import { XYPosition } from '@xyflow/react';

export interface LegacyFlow {
  id: string;
  name: string;
  comment?: string;
  startAt: string;
  states: Record<string, LegacyState>;
  version?: string;
  timeoutSeconds?: number | null;
  queryLanguage?: string;
}

export interface LegacyState {
  type: string;
  comment?: string;
  resource?: string;
  next?: string;
  end?: boolean;
  parameters?: Record<string, unknown>;
  choices?: LegacyChoice[];
  branches?: LegacyBranch[];
}

export interface LegacyChoice {
  variable?: string;
  next?: string;
  stringEquals?: string;
  numericEquals?: number;
  numericGreaterThan?: number;
  numericLessThan?: number;
  booleanEquals?: boolean;
  isNull?: boolean;
}

export interface LegacyBranch {
  comment?: string;
  startAt: string;
  states: Record<string, LegacyState>;
}

/**
 * Service for migrating legacy flows to the new canvas format.
 */
export const FlowMigrationService = {
  /**
   * Import a legacy flow definition into the canvas.
   * Clears existing nodes/edges first.
   */
  async importFlow(flow: LegacyFlow): Promise<void> {
    // Clear existing
    useNodeStore.setState({ nodes: [], selectedNodeId: null });
    useEdgeStore.setState({ edges: [] });

    const nodes: StepNode[] = [];
    const edges: Array<{ id: string; source: string; target: string }> = [];

    let y = 150;
    const nodeMap = new Map<string, string>(); // legacyId -> nodeId

    // Process states in order (respecting next links)
    const processed = new Set<string>();
    const processState = (stateId: string, x: number, currentY: number) => {
      if (processed.has(stateId)) return;
      processed.add(stateId);

      const state = flow.states[stateId];
      if (!state) return;

      const schemaId = resolveLegacyStateToSchema(state);
      const node = createNodeFromState(stateId, state, schemaId, { x, y: currentY });
      if (node) {
        nodes.push(node);
        nodeMap.set(stateId, node.id);
      }

      // Process next
      if (state.next && flow.states[state.next]) {
        edges.push({
          id: `edge-${stateId}-${state.next}`,
          source: nodeMap.get(stateId)!,
          target: nodeMap.get(state.next) || '',
        });
        processState(state.next, x + 100, currentY + 150);
      }

      // Process choices (branching)
      if (state.choices) {
        for (let i = 0; i < state.choices.length; i++) {
          const choice = state.choices[i];
          if (choice.next && flow.states[choice.next]) {
            edges.push({
              id: `edge-${stateId}-choice-${i}`,
              source: nodeMap.get(stateId)!,
              target: nodeMap.get(choice.next) || '',
            });
            processState(choice.next, x + (i - 1) * 300, currentY + 150);
          }
        }
      }

      // Process branches (parallel)
      if (state.branches) {
        for (let i = 0; i < state.branches.length; i++) {
          const branch = state.branches[i];
          if (branch.startAt && flow.states[branch.startAt]) {
            edges.push({
              id: `edge-${stateId}-branch-${i}`,
              source: nodeMap.get(stateId)!,
              target: nodeMap.get(branch.startAt) || '',
            });
            processState(branch.startAt, x + (i - 1) * 300, currentY + 150);
          }
        }
      }
    };

    // Start from the start state
    const startState = flow.startAt;
    if (startState && flow.states[startState]) {
      processState(startState, 250, 150);
    }

    // Also process any unconnected states
    for (const stateId of Object.keys(flow.states)) {
      if (!processed.has(stateId)) {
        processState(stateId, 250, y);
        y += 150;
      }
    }

    // Add nodes to store
    for (const node of nodes) {
      useNodeStore.getState().addNode(node.data.schemaId as string, node.position);
    }

    // Add edges
    for (const edge of edges) {
      if (edge.source && edge.target) {
        useEdgeStore.getState().addEdge({
          id: edge.id,
          source: edge.source,
          target: edge.target,
          type: 'step-edge',
        });
      }
    }
  },

  /**
   * Get a list of available legacy flows (from embedded data or API).
   */
  async listLegacyFlows(): Promise<{ id: string; name: string; description: string }[]> {
    // In production, this would read from the Flows/ directory via API
    return [
      { id: 'OrderProcess', name: 'Order Process', description: 'Order processing workflow with credit checks and AI review' },
      { id: 'REG_INT_BTP', name: 'Bank Transaction Processing', description: 'Bank file to authority payments processing pipeline' },
      { id: 'ParentFlow', name: 'Parent Flow', description: 'Demonstrates sub-flow composition' },
      { id: 'WebBrowserDemo', name: 'Web Browser Demo', description: 'Playwright web automation with AI analysis' },
    ];
  },

  /**
   * Get a legacy flow by ID (embedded sample data).
   */
  async getLegacyFlow(id: string): Promise<LegacyFlow | null> {
    const flows = getEmbeddedFlows();
    return flows[id] || null;
  },

  /**
   * Export current canvas as a legacy flow definition.
   */
  async exportToLegacy(): Promise<LegacyFlow> {
    const nodes = useNodeStore.getState().nodes;
    const edges = useEdgeStore.getState().edges;

    const states: Record<string, LegacyState> = {};
    let startState = '';

    for (const node of nodes) {
      const schemaId = node.data?.schemaId as string;
      const schema = schemaById.get(schemaId);
      const connectedEdges = edges.filter((e) => e.source === node.id);

      states[node.id] = {
        type: mapCategoryToLegacyType(schema?.category),
        comment: node.data?.description,
        resource: buildResourceUri(node),
        next: connectedEdges[0]?.target,
        end: connectedEdges.length === 0,
        parameters: node.data?.configuration || {},
      };

      if (!startState) startState = node.id;
    }

    return {
      id: 'exported-flow',
      name: 'Exported Flow',
      startAt: startState,
      states,
      version: '1.0',
    };
  },
};

// ── Helpers ──

function resolveLegacyStateToSchema(state: LegacyState): string {
  const resource = state.resource || '';
  const type = state.type || '';

  // Map legacy type/resource to schemaId
  if (resource.startsWith('ai://')) return 'stepflow:ai:decision';
  if (resource.startsWith('rule://')) return 'stepflow:rule:rule_engine';
  if (resource.startsWith('sql://')) return 'stepflow:data:sql';
  if (resource.startsWith('duckdb://')) return 'stepflow:data:duckdb';
  if (resource.startsWith('eav://')) return 'stepflow:data:eav';
  if (resource.startsWith('http://') || resource.startsWith('https://')) return 'stepflow:api:http';
  if (resource.startsWith('api://')) return 'stepflow:api:registered';
  if (resource.startsWith('transform://')) return 'stepflow:transform:jsonata';
  if (resource.startsWith('flow://')) return 'stepflow:subflow:invoke';
  if (resource.startsWith('tool://')) return 'stepflow:api:http';
  if (resource.startsWith('rules://')) return 'stepflow:rule:rule_engine';

  // Fallback by type
  if (type === 'Pass') return 'stepflow:utility:pass';
  if (type === 'Choice') return 'stepflow:rule:rule_engine';
  if (type === 'Parallel') return 'stepflow:utility:branch';
  if (type === 'Succeed') return 'stepflow:utility:pass';
  if (type === 'Task') return 'stepflow:api:http';

  return 'stepflow:utility:pass';
}

function createNodeFromState(
  stateId: string,
  state: LegacyState,
  schemaId: string,
  position: XYPosition
): StepNode | null {
  const schema = schemaById.get(schemaId);
  if (!schema) return null;

  return {
    id: `migrated-${stateId}`,
    type: schemaId,
    position,
    data: {
      schemaId,
      label: schema.name,
      color: schema.color,
      description: state.comment,
      configuration: state.parameters || {},
      isCollapsed: false,
      isDisabled: false,
    },
  };
}

function buildResourceUri(node: StepNode): string {
  const schemaId = node.data?.schemaId as string;
  const config = node.data?.configuration as Record<string, unknown> | undefined;

  if (schemaId?.startsWith('stepflow:ai:')) return `ai://${config?.llmService || 'azureOpenAI'}`;
  if (schemaId?.startsWith('stepflow:rule:')) return `rule://default`;
  if (schemaId?.startsWith('stepflow:data:sql')) return `sql://default`;
  if (schemaId?.startsWith('stepflow:data:duckdb')) return `duckdb://default`;
  if (schemaId?.startsWith('stepflow:api:http')) return `http://${config?.url || 'localhost'}`;
  if (schemaId?.startsWith('stepflow:api:registered')) return `api://${config?.apiId || 'default'}`;
  if (schemaId?.startsWith('stepflow:transform:')) return `transform://default`;
  if (schemaId?.startsWith('stepflow:subflow:')) return `flow://${config?.targetFlowId || 'default'}`;
  if (schemaId?.startsWith('stepflow:utility:')) return `utility://${schemaId.split(':').pop()}`;

  return `utility://default`;
}

function mapCategoryToLegacyType(category?: string): string {
  switch (category) {
    case 'ai': return 'Task';
    case 'rule': return 'Choice';
    case 'data': return 'Task';
    case 'api': return 'Task';
    case 'transform': return 'Task';
    case 'utility': return 'Pass';
    case 'subflow': return 'Task';
    default: return 'Pass';
  }
}

/**
 * Embedded sample flows for migration demo.
 * In production, these would be loaded from the Flows/ directory via API.
 */
function getEmbeddedFlows(): Record<string, LegacyFlow> {
  return {
    OrderProcess: {
      id: 'OrderProcess',
      name: 'Order Process',
      comment: 'Order Processing Workflow',
      startAt: 'ValidateOrder',
      states: {
        ValidateOrder: { type: 'Task', resource: 'http://validateorder', next: 'CheckCredit', comment: 'Validate Order' },
        CheckCredit: { type: 'Choice', next: 'ApproveOrder', comment: 'Credit Check', choices: [{ variable: '$.creditScore', next: 'AiReview', numericGreaterThan: 500 }] },
        AiReview: { type: 'Task', resource: 'ai://decision', next: 'ApproveOrder', comment: 'AI Credit Review' },
        ApproveOrder: { type: 'Task', resource: 'http://approveorder', next: 'RunRules', comment: 'Approve Order' },
        RunRules: { type: 'Task', resource: 'rule://ComplianceCheck', next: 'ProcessPayment', comment: 'Run Compliance Rules' },
        ProcessPayment: { type: 'Task', resource: 'https://api.payment.example.com/charge', next: 'SendConfirmation', comment: 'Process Payment' },
        SendConfirmation: { type: 'Task', resource: 'https://api.mail.example.com/send', end: true, comment: 'Send Confirmation Email' },
      },
    },
    REG_INT_BTP: {
      id: 'REG_INT_BTP',
      name: 'Bank Transaction Processing',
      comment: 'Bank file to authority payments processing',
      startAt: 'LoadTransactionData',
      states: {
        LoadTransactionData: { type: 'Task', resource: 'transform://query', next: 'LoadReferenceData', comment: 'Load transactions' },
        LoadReferenceData: { type: 'Task', resource: 'transform://query', next: 'Phase1_Corrections', comment: 'Load reference data' },
        Phase1_Corrections: { type: 'Task', resource: 'transform://execute', next: 'Phase2_SetCodes', comment: 'Initial corrections' },
        Phase2_SetCodes: { type: 'Task', resource: 'transform://execute', next: 'Phase3_ParallelValidation', comment: 'Set codes' },
        Phase3_ParallelValidation: { type: 'Parallel', next: 'Phase4_FinalSummary', comment: 'Parallel validation' },
        Phase4_FinalSummary: { type: 'Task', resource: 'transform://query', end: true, comment: 'Final summary' },
      },
    },
    ParentFlow: {
      id: 'ParentFlow',
      name: 'Parent Flow',
      comment: 'Demonstrates sub-flow composition',
      startAt: 'PrepareInput',
      states: {
        PrepareInput: { type: 'Pass', next: 'RunComplianceCheck', comment: 'Prepare input data' },
        RunComplianceCheck: { type: 'Task', resource: 'flow://SprayComplianceCheck', end: true, comment: 'Call sub-flow' },
      },
    },
    WebBrowserDemo: {
      id: 'WebBrowserDemo',
      name: 'Web Browser Demo',
      comment: 'Playwright web automation with AI analysis',
      startAt: 'SearchAI',
      states: {
        SearchAI: { type: 'Task', resource: 'tool://web_browser', next: 'AnalyzeResults', comment: 'Search with browser' },
        AnalyzeResults: { type: 'Task', resource: 'ai://decide/LMStudio', next: 'NavigateToProfile', comment: 'AI analysis' },
        NavigateToProfile: { type: 'Task', resource: 'tool://web_browser', end: true, comment: 'Navigate to profile' },
      },
    },
  };
}
