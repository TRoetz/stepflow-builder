import { create } from 'zustand';
import { Node, Edge } from '@xyflow/react';

interface UndoRedoState {
  undoStack: Array<{ nodes: Node[]; edges: Edge[] }>;
  redoStack: Array<{ nodes: Node[]; edges: Edge[] }>;
  canUndo: boolean;
  canRedo: boolean;
  maxHistory: number;

  // Actions
  pushState: (nodes: Node[], edges: Edge[]) => void;
  undo: () => void;
  redo: () => void;
  clearHistory: () => void;
}

const MAX_HISTORY = 50;

export const useUndoRedoStore = create<UndoRedoState>((set, get) => ({
  undoStack: [],
  redoStack: [],
  canUndo: false,
  canRedo: false,
  maxHistory: MAX_HISTORY,

  pushState: (nodes: Node[], edges: Edge[]) => {
    const state = {
      nodes: JSON.parse(JSON.stringify(nodes)),
      edges: JSON.parse(JSON.stringify(edges)),
    };

    set((prev) => {
      const newUndo = [...prev.undoStack, state];
      // Limit history size
      if (newUndo.length > prev.maxHistory) {
        newUndo.shift();
      }
      return {
        undoStack: newUndo,
        redoStack: [], // Clear redo on new action
        canUndo: newUndo.length > 1,
        canRedo: false,
      };
    });
  },

  undo: () => {
    const { undoStack, redoStack } = get();
    if (undoStack.length <= 1) return;

    const currentState = undoStack.pop();
    if (!currentState) return;

    const previousState = undoStack[undoStack.length - 1];
    if (!previousState) return;

    // Push current to redo
    redoStack.push(currentState);

    set({
      undoStack,
      redoStack,
      canUndo: undoStack.length > 1,
      canRedo: redoStack.length > 0,
    });

    // Restore previous state in node/edge stores
    // This is called externally by the canvas component
  },

  redo: () => {
    const { undoStack, redoStack } = get();
    if (redoStack.length === 0) return;

    const nextState = redoStack.pop();
    if (!nextState) return;

    undoStack.push(nextState);

    set({
      undoStack,
      redoStack,
      canUndo: true,
      canRedo: redoStack.length > 0,
    });
  },

  clearHistory: () => {
    set({
      undoStack: [],
      redoStack: [],
      canUndo: false,
      canRedo: false,
    });
  },
}));
