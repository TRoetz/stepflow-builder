import { create } from 'zustand';
import { Edge, EdgeChange } from '@xyflow/react';

// ── Extended Edge type ──
export type StepEdge = Edge;

interface EdgeState {
  edges: StepEdge[];
  onEdgesChange: (changes: EdgeChange[]) => void;
  addEdge: (edge: StepEdge) => void;
  removeEdge: (edgeId: string) => void;
  removeEdgesByNodeId: (nodeId: string) => void;
  clearEdges: () => void;
}

export const useEdgeStore = create<EdgeState>((set, get) => ({
  edges: [],

  // ── Edge Change Handler (xyflow) ──
  onEdgesChange: (changes: EdgeChange[]) => {
    set({
      edges: applyEdgeChangesWithSchema(changes, get().edges),
    });
  },

  // ── Add Edge ──
  addEdge: (edge: StepEdge) => {
    set({
      edges: [...get().edges, edge],
    });
  },

  // ── Remove Edge ──
  removeEdge: (edgeId: string) => {
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
    set({ edges: [] });
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
          return { ...edge, selected: true };
        }
        return edge;
      });
    }
  }

  return nextEdges;
}
