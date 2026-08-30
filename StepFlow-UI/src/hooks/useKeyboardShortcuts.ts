import { useEffect, useCallback } from 'react';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useUndoRedoStore } from '@stores/useUndoRedoStore';

interface KeyboardShortcutsOptions {
  onAutoLayout?: () => void;
  onSave?: () => void;
  onRun?: () => void;
}

/**
 * Hook for keyboard shortcuts (same set as buildspec).
 */
export function useKeyboardShortcuts(options: KeyboardShortcutsOptions = {}) {
  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      const { ctrlKey, shiftKey, key } = event;
      const isCtrl = ctrlKey || false; // Handle both platforms

      // Skip if user is typing in an input
      const target = event.target as HTMLElement;
      if (target?.tagName === 'INPUT' || target?.tagName === 'TEXTAREA' || target?.tagName === 'SELECT') {
        return;
      }

      // Ctrl+Z — Undo
      if (isCtrl && key === 'z' && !shiftKey) {
        event.preventDefault();
        useUndoRedoStore.getState().undo();
        return;
      }

      // Ctrl+Y / Ctrl+Shift+Z — Redo
      if (isCtrl && (key === 'y' || (key === 'z' && shiftKey))) {
        event.preventDefault();
        useUndoRedoStore.getState().redo();
        return;
      }

      // Ctrl+S — Save
      if (isCtrl && key === 's') {
        event.preventDefault();
        options.onSave?.();
        return;
      }

      // Ctrl+Enter — Run
      if (isCtrl && key === 'Enter') {
        event.preventDefault();
        options.onRun?.();
        return;
      }

      // Ctrl+Shift+F — Auto-Layout
      if (isCtrl && shiftKey && key === 'F') {
        event.preventDefault();
        options.onAutoLayout?.();
        return;
      }

      // Ctrl+A — Select All (handled by xyflow)
      // Ctrl+C / Ctrl+V — Copy/Paste (handled by xyflow)
      // Ctrl+D — Duplicate
      if (isCtrl && key === 'd') {
        event.preventDefault();
        const selectedId = useNodeStore.getState().selectedNodeId;
        if (selectedId) {
          useNodeStore.getState().duplicateNode(selectedId);
        }
        return;
      }

      // Delete / Backspace — Delete selected
      if (key === 'Delete' || key === 'Backspace') {
        event.preventDefault();
        const state = useEdgeStore.getState();
        const selectedEdge = state.edges.find((e) => e.selected);
        if (selectedEdge) {
          useEdgeStore.getState().removeEdge(selectedEdge.id);
          return;
        }
        const selectedId = useNodeStore.getState().selectedNodeId;
        if (selectedId) {
          // Remove connected edges
          useEdgeStore.getState().removeEdgesByNodeId(selectedId);
          useNodeStore.getState().removeNode(selectedId);
        }
        return;
      }

      // Escape — Deselect
      if (key === 'Escape') {
        event.preventDefault();
        useNodeStore.getState().setSelectedNode(null);
        return;
      }
    },
    [options]
  );

  useEffect(() => {
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [handleKeyDown]);
}
