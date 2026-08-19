import { Handle, Position } from '@xyflow/react';
import { NodeData } from '@schema-types/schema';

// ── Placeholder Default Node (replaced by category-specific nodes in Phase 3) ──
export function DefaultNode(props: { data?: NodeData }) {
  const data = props.data as NodeData | undefined;
  const color = (data?.color as string) || '#6366f1';

  return (
    <div className="px-4 py-3 rounded-xl bg-gray-800/90 border border-gray-700 shadow-lg min-w-[160px]">
      <Handle type="target" position={Position.Left} style={{ width: 20, height: 20, background: color }} />
      <div className="text-sm font-medium" style={{ color }}>
        {data?.label || 'Step Node'}
      </div>
      <Handle type="source" position={Position.Right} style={{ width: 20, height: 20, background: color }} />
    </div>
  );
}
