import { create } from 'zustand';
import { Node, NodeChange, applyNodeChanges } from '@xyflow/react';
import { v4 as uuidv4 } from 'uuid';
import { schemaById } from '@schemas/index';
import { useEdgeStore } from '@stores/useEdgeStore';
import { NodeData } from '@schema-types/schema';

// ── Extended Node with schema data ──
export type StepNode = Node<NodeData>;

interface NodeState {
  nodes: StepNode[];
  selectedNodeId: string | null;
  onNodesChange: (changes: NodeChange[]) => void;
  /** Adds a node from a schema and returns it (null if the schema is unknown). */
  addNode: (
    schemaId: string,
    position?: { x: number; y: number }
  ) => StepNode | null;
  removeNode: (nodeId: string) => void;
  updateNodeData: (nodeId: string, data: Partial<NodeData>) => void;
  setSelectedNode: (nodeId: string | null) => void;
  getSelectedNode: () => StepNode | undefined;
  duplicateNode: (nodeId: string) => void;
  clearNodes: () => void;
}

export const useNodeStore = create<NodeState>((set, get) => ({
  nodes: [],
  selectedNodeId: null,

  // ── Node Change Handler (xyflow) ──
  onNodesChange: (changes: NodeChange[]) => {
    set({
      nodes: applyNodeChangesWithSchema(changes, get().nodes),
    });
  },

  // ── Add Node from Schema ──
  addNode: (schemaId: string, position?: { x: number; y: number }) => {
    const schema = schemaById.get(schemaId);
    if (!schema) {
      console.warn(`Schema not found: ${schemaId}`);
      return null;
    }

    const defaultConfig: Record<string, unknown> = {};
    for (const field of schema.configFields) {
      if (field.default !== undefined) {
        defaultConfig[field.id] = field.default;
      }
    }

    const newNode: StepNode = {
      id: uuidv4(),
      type: schemaId,
      position: position ?? { x: 250, y: 150 },
      data: {
        schemaId,
        label: schema.name,
        color: schema.color,
        description: schema.description,
        configuration: defaultConfig,
        isCollapsed: false,
        isDisabled: false,
      },
    };

    set({
      nodes: [...get().nodes, newNode],
    });

    return newNode;
  },

  // ── Remove Node ──
  removeNode: (nodeId: string) => {
    set({
      nodes: get().nodes.filter((n) => n.id !== nodeId),
      selectedNodeId: get().selectedNodeId === nodeId ? null : get().selectedNodeId,
    });
    // Remove every edge touching this node so no dangling edges remain.
    useEdgeStore.getState().removeEdgesByNodeId(nodeId);
  },

  // ── Update Node Data ──
  updateNodeData: (nodeId: string, data: Partial<NodeData>) => {
    set({
      nodes: get().nodes.map((node) =>
        node.id === nodeId
          ? { ...node, data: { ...node.data, ...data } }
          : node
      ),
    });
  },

  // ── Set Selected Node ──
  setSelectedNode: (nodeId: string | null) => {
    set({ selectedNodeId: nodeId });
  },

  // ── Get Selected Node ──
  getSelectedNode: () => {
    return get().nodes.find((n) => n.id === get().selectedNodeId);
  },

  // ── Duplicate Node ──
  duplicateNode: (nodeId: string) => {
    const source = get().nodes.find((n) => n.id === nodeId);
    if (!source) return;

    const newNode: StepNode = {
      ...source,
      id: uuidv4(),
      position: {
        x: source.position.x + 20,
        y: source.position.y + 20,
      },
      data: {
        ...source.data,
        label: `${source.data.label} (copy)`,
      },
    };

    set({
      nodes: [...get().nodes, newNode],
    });
  },

  // ── Clear Nodes ──
  clearNodes: () => {
    set({ nodes: [], selectedNodeId: null });
  },
}));

// ── Apply Node Changes (uses ReactFlow's built-in utility) ─
function applyNodeChangesWithSchema(
  changes: NodeChange[],
  nodes: StepNode[]
): StepNode[] {
  // Use ReactFlow's applyNodeChanges to properly handle all change types
  // including remove, position, dimensions, select, resize, extent, etc.
  return applyNodeChanges(changes, nodes) as StepNode[];
}
