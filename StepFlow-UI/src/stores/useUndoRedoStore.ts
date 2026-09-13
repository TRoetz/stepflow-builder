import { create } from 'zustand';
import { Edge, Node } from '@xyflow/react';
import type { StepNode } from '@stores/useNodeStore';
import type { StepEdge } from '@stores/useEdgeStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';

export interface CanvasSnapshot {
  nodes: Node[];
  edges: Edge[];
}

interface UndoRedoState {
  /** Pre-change snapshots: stack top = state to restore on next undo(). */
  undoStack: CanvasSnapshot[];
  /** Post-change snapshots captured by undo(): stack top = redo() target. */
  redoStack: CanvasSnapshot[];
  canUndo: boolean;
  canRedo: boolean;
  maxHistory: number;

  /**
   * Records the CURRENT canvas so undo() can restore it.
   * Call immediately BEFORE a canvas mutation.
   * Consecutive pushes with the same `coalesceKey` inside the edit window
   * (typing in one field) collapse into a single undo step.
   */
  pushSnapshot: (coalesceKey?: string) => void;
  undo: () => void;
  redo: () => void;
  clearHistory: () => void;
}

const MAX_HISTORY = 50;
const COALESCE_WINDOW_MS = 800;

function currentCanvas(): CanvasSnapshot {
  // Deep copy: live node objects mutate in place later; snapshots must stay frozen.
  return {
    nodes: JSON.parse(JSON.stringify(useNodeStore.getState().nodes)),
    edges: JSON.parse(JSON.stringify(useEdgeStore.getState().edges)),
  };
}

// Typing-burst tracking for pushSnapshot coalescing (outside the store:
// transient bookkeeping, never rendered).
let lastPushKey: string | null = null;
let lastPushAt = 0;

export const useUndoRedoStore = create<UndoRedoState>((set, get) => ({
  undoStack: [],
  redoStack: [],
  canUndo: false,
  canRedo: false,
  maxHistory: MAX_HISTORY,

  pushSnapshot: (coalesceKey?: string) => {
    const now = Date.now();
    if (
      coalesceKey !== undefined &&
      coalesceKey === lastPushKey &&
      now - lastPushAt < COALESCE_WINDOW_MS &&
      get().undoStack.length > 0
    ) {
      // Same field edited again within the window: one undo step stays.
      lastPushAt = now;
      return;
    }
    lastPushKey = coalesceKey ?? null;
    lastPushAt = now;

    const snapshot = currentCanvas();
    set((prev) => {
      const undoStack = [...prev.undoStack, snapshot];
      if (undoStack.length > prev.maxHistory) undoStack.shift();
      return { undoStack, redoStack: [], canUndo: true, canRedo: false };
    });
  },

  undo: () => {
    const { undoStack } = get();
    if (undoStack.length === 0) return;

    const nextUndo = undoStack.slice(0, -1);
    const target = undoStack[undoStack.length - 1];

    set((prev) => {
      const redoStack = [...prev.redoStack, currentCanvas()];
      if (redoStack.length > prev.maxHistory) redoStack.shift();
      return {
        undoStack: nextUndo,
        redoStack,
        canUndo: nextUndo.length > 0,
        canRedo: true,
      };
    });

    // Restore into the live stores (selection cleared: target ids may no longer exist).
    useNodeStore.setState({ nodes: target.nodes as StepNode[], selectedNodeId: null });
    useEdgeStore.setState({ edges: target.edges as StepEdge[], selectedEdgeId: null });
    lastPushKey = null;
  },

  redo: () => {
    const { redoStack } = get();
    if (redoStack.length === 0) return;

    const nextRedo = redoStack.slice(0, -1);
    const target = redoStack[redoStack.length - 1];

    set((prev) => {
      const undoStack = [...prev.undoStack, currentCanvas()];
      if (undoStack.length > prev.maxHistory) undoStack.shift();
      return {
        undoStack,
        redoStack: nextRedo,
        canUndo: true,
        canRedo: nextRedo.length > 0,
      };
    });

    useNodeStore.setState({ nodes: target.nodes as StepNode[], selectedNodeId: null });
    useEdgeStore.setState({ edges: target.edges as StepEdge[], selectedEdgeId: null });
    lastPushKey = null;
  },

  clearHistory: () => {
    lastPushKey = null;
    lastPushAt = 0;
    set({
      undoStack: [],
      redoStack: [],
      canUndo: false,
      canRedo: false,
    });
  },
}));
