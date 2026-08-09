import { BaseEdge, getSmoothStepPath, type EdgeProps } from '@xyflow/react';

// ═══════════════════════════════════════════════════════════
// Step Edge — Custom edge with category-aware coloring
// ═══════════════════════════════════════════════════════════

export function StepEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  markerEnd,
  data,
}: EdgeProps) {
  const [path] = getSmoothStepPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  // Extract data from edge data prop
  const edgeData = data as {
    sourceColor?: string;
    isValid?: boolean;
    warning?: boolean;
  } | undefined;

  // Determine edge color
  let edgeColor = '#6366f1';
  if (edgeData?.warning) {
    edgeColor = '#f59e0b';
  } else if (edgeData?.isValid === false) {
    edgeColor = '#ef4444';
  } else if (edgeData?.sourceColor) {
    edgeColor = edgeData.sourceColor;
  }

  return (
    <BaseEdge
      path={path}
      style={{
        stroke: edgeColor,
        strokeWidth: edgeData?.warning ? 2 : 2.5,
        strokeDasharray: edgeData?.warning ? '5 5' : undefined,
      }}
      markerEnd={markerEnd ? { ...(typeof markerEnd as any), color: edgeColor } : undefined}
    />
  );
}
