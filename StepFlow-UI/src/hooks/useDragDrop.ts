import { useState, useCallback } from 'react';

/**
 * Hook for drag-and-drop from palette to canvas.
 * Reusable across components.
 */
export function useDragDrop() {
  const [draggedSchemaId, setDraggedSchemaId] = useState<string | null>(null);

  const onDragStart = useCallback((e: React.DragEvent, schemaId: string) => {
    e.dataTransfer.setData('application/stepflow-schema', schemaId);
    e.dataTransfer.effectAllowed = 'move';
    setDraggedSchemaId(schemaId);
  }, []);

  const onDragEnd = useCallback(() => {
    setDraggedSchemaId(null);
  }, []);

  const getDraggedSchemaId = useCallback(() => {
    return draggedSchemaId;
  }, [draggedSchemaId]);

  return {
    draggedSchemaId,
    onDragStart,
    onDragEnd,
    getDraggedSchemaId,
  };
}
