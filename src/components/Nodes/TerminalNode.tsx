import { Handle, Position } from '@xyflow/react';
import { NodeData } from '@schema-types/schema';
import { useExecutionStore } from '@stores/useExecutionStore';
import { getExecutionColor } from '@utils/validation';

interface TerminalNodeProps {
  id: string;
  data: NodeData & {
    isStart?: boolean;
  };
  selected?: boolean;
}

export function TerminalNode({ id, data, selected }: TerminalNodeProps) {
  const isStart = data.schemaId === 'stepflow:terminal:start';
  const accentColor = isStart ? '#10B981' : '#EF4444';
  const bgColor = isStart ? 'rgba(16, 185, 129, 0.08)' : 'rgba(239, 68, 68, 0.08)';

  // Read execution state directly from store
  const completedNodes = useExecutionStore((s) => s.completedNodes);
  const failedNodes = useExecutionStore((s) => s.failedNodes);
  const currentNodeId = useExecutionStore((s) => s.currentNodeId);

  let executionBorderColor: string | undefined;
  if (failedNodes.has(id)) {
    executionBorderColor = getExecutionColor('failed');
  } else if (completedNodes.has(id)) {
    executionBorderColor = getExecutionColor('completed');
  } else if (currentNodeId === id) {
    executionBorderColor = getExecutionColor('running');
  }

  const borderColor = executionBorderColor || (selected
    ? accentColor
    : isStart
      ? 'rgba(16, 185, 129, 0.4)'
      : 'rgba(239, 68, 68, 0.4)');

  return (
    <div
      className="terminal-node"
      style={{
        border: `2px solid ${borderColor}`,
        backgroundColor: bgColor,
        borderRadius: '9999px',
        padding: '12px 24px',
        minWidth: '100px',
        textAlign: 'center',
        boxShadow: selected ? `0 0 0 2px ${accentColor}30` : 'none',
        transition: 'all 0.15s ease',
      }}
    >
      {isStart ? (
        <Handle
          type="source"
          position={Position.Right}
          id="start_output"
          style={{
            background: accentColor,
            borderColor: accentColor,
            top: '50%',
            right: '-8px',
            width: 24,
            height: 24,
          }}
        />
      ) : (
        <Handle
          type="target"
          position={Position.Left}
          id="end_input"
          style={{
            background: accentColor,
            borderColor: accentColor,
            top: '50%',
            left: '-8px',
            width: 24,
            height: 24,
          }}
        />
      )}

      <div className="flex items-center justify-center gap-2">
        <div
          className="w-3 h-3 rounded-full shrink-0"
          style={{ backgroundColor: accentColor }}
        />
        <span
          className="text-sm font-bold tracking-wider"
          style={{ color: accentColor }}
        >
          {isStart ? '▶ START' : '■ END'}
        </span>
      </div>

      {data.description && (
        <div className="text-xs text-gray-500 mt-1 truncate max-w-[200px]">
          {data.description as string}
        </div>
      )}
    </div>
  );
}
