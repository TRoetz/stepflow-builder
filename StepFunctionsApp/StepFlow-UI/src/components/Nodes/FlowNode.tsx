import { Handle, Position } from '@xyflow/react';
import { NodeData } from '@schema-types/schema';
import { useExecutionStore } from '@stores/useExecutionStore';
import { getExecutionColor } from '@utils/validation';
import {
  GitBranch,
  Layers,
  GitCompare,
  CheckCircle,
  XCircle,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';

interface FlowNodeProps {
  id: string;
  data: NodeData;
  selected?: boolean;
}

const schemaIcons: Record<string, React.ReactNode> = {
  'stepflow:flow:choice': <GitBranch className="w-4 h-4" />,
  'stepflow:flow:map': <Layers className="w-4 h-4" />,
  'stepflow:flow:parallel': <GitCompare className="w-4 h-4" />,
  'stepflow:flow:succeed': <CheckCircle className="w-4 h-4" />,
  'stepflow:flow:fail': <XCircle className="w-4 h-4" />,
};

const schemaColors: Record<string, string> = {
  'stepflow:flow:choice': '#8B5CF6',
  'stepflow:flow:map': '#8B5CF6',
  'stepflow:flow:parallel': '#8B5CF6',
  'stepflow:flow:succeed': '#10B981',
  'stepflow:flow:fail': '#EF4444',
};

export function FlowNode({ id, data, selected }: FlowNodeProps) {
  const isCollapsed = data.isCollapsed as boolean | undefined;
  const accentColor = schemaColors[data.schemaId as string] || '#8B5CF6';
  const icon = schemaIcons[data.schemaId as string] || <GitBranch className="w-4 h-4" />;

  // Read execution state directly from store
  const completedNodes = useExecutionStore((s) => s.completedNodes);
  const failedNodes = useExecutionStore((s) => s.failedNodes);
  const currentNodeId = useExecutionStore((s) => s.currentNodeId);

  let executionBorderColor: string | undefined;
  let executionClass = '';
  if (failedNodes.has(id)) {
    executionBorderColor = getExecutionColor('failed');
    executionClass = 'invalid';
  } else if (completedNodes.has(id)) {
    executionBorderColor = getExecutionColor('completed');
    executionClass = 'valid';
  } else if (currentNodeId === id) {
    executionBorderColor = getExecutionColor('running');
    executionClass = 'executing';
  }

  const isTerminal = ['stepflow:flow:succeed', 'stepflow:flow:fail'].includes(data.schemaId as string);
  const hasInput = true;
  const hasOutput = !isTerminal;

  return (
    <div
      className={`step-node ${executionClass}`}
      style={{
        border: `1px solid ${executionBorderColor || (selected ? accentColor : 'rgba(139, 92, 246, 0.3)')}`,
        borderRadius: '8px',
        backgroundColor: '#0f1117',
        minWidth: '180px',
        boxShadow: selected ? `0 0 0 2px ${accentColor}20` : 'none',
      }}
    >
      {hasInput && (
        <Handle
          type="target"
          position={Position.Left}
          id="input_data"
          style={{ background: accentColor, borderColor: accentColor, width: 20, height: 20 }}
        />
      )}

      {hasOutput && data.schemaId === 'stepflow:flow:choice' && (
        <>
          <Handle
            type="source"
            position={Position.Right}
            id="output_true"
            style={{ background: '#10B981', borderColor: '#10B981', top: '35%', width: 20, height: 20 }}
          />
          <Handle
            type="source"
            position={Position.Right}
            id="output_false"
            style={{ background: '#EF4444', borderColor: '#EF4444', top: '65%', width: 20, height: 20 }}
          />
        </>
      )}
      {hasOutput && data.schemaId !== 'stepflow:flow:choice' && (
        <Handle
          type="source"
          position={Position.Right}
          id="output"
          style={{ background: accentColor, borderColor: accentColor, width: 20, height: 20 }}
        />
      )}

      <div
        className="flex items-center gap-2 px-3 py-2 border-b"
        style={{ borderColor: 'rgba(139, 92, 246, 0.15)', backgroundColor: 'rgba(139, 92, 246, 0.05)' }}
      >
        <div style={{ color: accentColor }}>
          {icon}
        </div>
        <span className="text-sm font-semibold text-gray-200">{data.label as string}</span>
        <div className="flex-1" />
        <button
          className="text-gray-500 hover:text-gray-300 transition-colors"
          onClick={() => {}}
        >
          {isCollapsed ? <ChevronRight className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
        </button>
      </div>

      {!isCollapsed && (
        <div className="px-3 py-2">
          <div className="text-xs text-gray-500">
            Flow state — controls execution path
          </div>
        </div>
      )}
    </div>
  );
}
