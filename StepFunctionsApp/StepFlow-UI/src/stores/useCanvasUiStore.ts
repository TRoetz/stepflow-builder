import { create } from 'zustand';

/**
 * Transient canvas-UI state (P0: "Add next step" popover).
 * Kept separate from node/edge stores so opening/closing the popover never
 * invalidates canvas renders.
 */
export interface AddNextAnchor {
  /** Node the new step will be chained after */
  nodeId: string;
  /** Viewport (client) coordinates of the chip's bottom-right corner */
  x: number;
  y: number;
}

interface CanvasUiState {
  addNextAnchor: AddNextAnchor | null;
  openAddNext: (nodeId: string, x: number, y: number) => void;
  closeAddNext: () => void;
}

export const useCanvasUiStore = create<CanvasUiState>((set) => ({
  addNextAnchor: null,
  openAddNext: (nodeId, x, y) => set({ addNextAnchor: { nodeId, x, y } }),
  closeAddNext: () => set({ addNextAnchor: null }),
}));
