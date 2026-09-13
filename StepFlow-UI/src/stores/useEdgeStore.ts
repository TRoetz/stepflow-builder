import { create } from 'zustand';
import { Edge, EdgeChange } from '@xyflow/react';
import { useUndoRedoStore } from '@stores/useUndoRedoStore';

// ── Extended Edge type ──
export type StepEdge = Edge;

interface EdgeState {
  edges: StepEdge[];
  selectedEdgeId: string | null;
  onEdgesChange: (changes: EdgeChange[]) => void;
  addEdge: (edge: StepEdge) => void;
  removeEdge: (edgeId: string) => void;
  removeEdgesByNodeId: (nodeId: string) => void;
  clearEdges: () => void;
  setSelectedEdge: (edgeId: string | null) => void;
}

export const useEdgeStore = create<EdgeState>((set, get) => ({
  edges: [],
  selectedEdgeId: null,
  // ── Edge Change Handler (xyflow) ──
  onEdgesChange: (changes: EdgeChange[]) => {
    if (changes.some((c) => c.type === 'remove')) {
      useUndoRedoStore.getState().pushSnapshot();
    }
    set({
      edges: applyEdgeChangesWithSchema(changes, get().edges),
    });
  },

  // ── Add Edge ──
  addEdge: (edge: StepEdge) => {
    // Snapshot before mutation so undo() removes the connection.
    useUndoRedoStore.getState().pushSnapshot();
    set({
      edges: [...get().edges, edge],
    });
  },

  // ── Remove Edge ──
  removeEdge: (edgeId: string) => {
    // Snapshot before mutation so undo() restores the connection.
    // NOTE: removeEdgesByNodeId stays snapshot-free on purpose — node
    // removal already snapshots, and callers must not pay twice.
    useUndoRedoStore.getState().pushSnapshot();
    set({
      edges: get().edges.filter((e) => e.id !== edgeId),
    });
  },

  // ── Remove Edges Connected to Node ──
  removeEdgesByNodeId: (nodeId: string) => {
    set({
      edges: get().edges.filter(
        (e) => e.source !== nodeId && e.target !== nodeId
      ),
    });
  },

  // ── Clear All Edges ──
  clearEdges: () => {
    useUndoRedoStore.getState().pushSnapshot();
    set({ edges: [] });
  },

  // ── Set Selected Edge ──
  setSelectedEdge: (edgeId: string | null) => {
    set({ selectedEdgeId: edgeId });
  },
}));

// ── Apply Edge Changes ──
function applyEdgeChangesWithSchema(
  changes: EdgeChange[],
  edges: StepEdge[]
): StepEdge[] {
  let nextEdges = edges;

  for (const change of changes) {
    if (change.type === 'remove') {
      nextEdges = nextEdges.filter((e) => e.id !== change.id);
    } else if (change.type === 'select') {
      nextEdges = nextEdges.map((edge) => {
        if (edge.id === change.id) {
          return { ...edge, selected: change.selected };
        }
        return edge;
      });
    }
  }

  return nextEdges;
}
