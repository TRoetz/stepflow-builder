import { XYPosition } from '@xyflow/react';

/**
 * Calculate a grid position for a new node based on existing nodes.
 */
export function calculateNextPosition(nodes: { position: XYPosition }[], _gridSize: number = 16): XYPosition {
  if (nodes.length === 0) return { x: 250, y: 150 };

  // Find the rightmost and bottommost positions
  const maxX = Math.max(...nodes.map((n) => n.position.x));
  const maxY = Math.max(...nodes.map((n) => n.position.y));

  // Place new node below the last node, aligned to grid
  return {
    x: (maxX + 240) % 800 < 400 ? (maxX + 240) : 100,
    y: maxY + 120,
  };
}

/**
 * Snap a position to the grid.
 */
export function snapToGrid(position: XYPosition, gridSize: number = 16): XYPosition {
  return {
    x: Math.round(position.x / gridSize) * gridSize,
    y: Math.round(position.y / gridSize) * gridSize,
  };
}

/**
 * Calculate the bounding box of a set of nodes.
 */
export function calculateBoundingBox(
  nodes: { position: { x: number; y: number }; measured?: { width?: number; height?: number } }[]
): { x: number; y: number; width: number; height: number } {
  if (nodes.length === 0) return { x: 0, y: 0, width: 0, height: 0 };

  const defaultWidth = 240;
  const defaultHeight = 100;

  const minX = Math.min(...nodes.map((n) => n.position.x));
  const minY = Math.min(...nodes.map((n) => n.position.y));
  const maxX = Math.max(...nodes.map((n) => n.position.x + (n.measured?.width || defaultWidth)));
  const maxY = Math.max(...nodes.map((n) => n.position.y + (n.measured?.height || defaultHeight)));

  return {
    x: minX,
    y: minY,
    width: maxX - minX,
    height: maxY - minY,
  };
}

/**
 * Center nodes around a viewport position.
 */
export function centerNodes(
  nodes: { position: XYPosition }[],
  center: XYPosition
): { position: XYPosition }[] {
  const bounds = calculateBoundingBox(nodes);
  const offsetX = center.x - bounds.x - bounds.width / 2;
  const offsetY = center.y - bounds.y - bounds.height / 2;

  return nodes.map((node) => ({
    ...node,
    position: {
      x: node.position.x + offsetX,
      y: node.position.y + offsetY,
    },
  }));
}
