import { useState, useCallback, useMemo } from 'react';
import { Handle, Position } from '@xyflow/react';
import { ChevronDown, ChevronRight, X, AlertCircle, CheckCircle, Loader2, Plus } from 'lucide-react';
import { NodeData, StepInput, StepOutput, DataType } from '@schema-types/schema';
import { schemaById } from '@schemas/index';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { getConfigIssues, getGraphHintsForNode } from '@hooks/useFlowValidation';
import { useCanvasUiStore } from '@stores/useCanvasUiStore';
import { useExecutionStore } from '@stores/useExecutionStore';
import { getExecutionColor } from '@utils/validation';

// ═══════════════════════════════════════════════════════════
// Handle Style Helpers
// ═══════════════════════════════════════════════════════════

// Port dot sizes (px). Kept large so ports are easy to grab when connecting.
export const HANDLE_SIZES: Record<DataType, number> = {
  json: 24,
  string: 20,
  number: 20,
  boolean: 19,
  array: 24,
  image: 21,
  any: 20,
};

export const HANDLE_COLORS: Record<DataType, string> = {
  json: '#8b5cf6',
  string: '#60a599',
  number: '#f4d03f',
  boolean: '#e74c3c',
  array: '#3498db',
  image: '#e67e22',
  any: '#95a5a6',
};

// ═══════════════════════════════════════════════════════════
// Handle Label Component
// ═══════════════════════════════════════════════════════════

function HandleLabel({
  label,
  type,
  optional,
  side,
}: {
  label: string;
  type: DataType;
  optional: boolean;
  side: 'left' | 'right';
}) {
  const sideClass = side === 'left' ? 'pr-1' : 'pl-1';
  return (
    <div className={`flex items-center gap-1.5 text-[10px] ${sideClass} whitespace-nowrap`}>
      <span
        className="flex items-center justify-center shrink-0"
        style={{
          width: 8,
          height: 8,
          borderRadius: type === 'boolean' ? '1px' : '50%',
          backgroundColor: HANDLE_COLORS[type],
          opacity: 0.7,
        }}
      />
      <span className="text-gray-400">{label}</span>
      {optional && <span className="text-gray-600">?</span>}
    </div>
  );
}

// ═══════════════════════════════════════════════════════════
// Base Node with Handles — Used by category nodes
// ═══════════════════════════════════════════════════════════

export function BaseNodeWithHandles({
  id,
  data,
  selected,
  inputs,
  outputs,
  children,
  headerExtras,
  footerExtras,
}: {
  id: string;
  data: NodeData;
  selected?: boolean;
  inputs: StepInput[];
  outputs: StepOutput[];
  children: React.ReactNode;
  headerExtras?: React.ReactNode;
  footerExtras?: React.ReactNode;
}) {
  const [collapsed, setCollapsed] = useState(() => data.isCollapsed ?? false);
  const schema = schemaById.get(data.schemaId as string);
  const accentColor = (data.color as string) || schema?.color || '#6366f1';
  const disabled = data.isDisabled as boolean;
  const updateNodeData = useNodeStore((s) => s.updateNodeData);
  const openAddNext = useCanvasUiStore((s) => s.openAddNext);

  // P5: flow-health issues for this node (required fields, schema rules and
  // lineage hints) — same source of truth as the header indicator.
  const graphNodes = useNodeStore((s) => s.nodes);
  const edges = useEdgeStore((s) => s.edges);
  const healthIssues = useMemo(
    () => [...getConfigIssues(data), ...getGraphHintsForNode(id, graphNodes, edges)],
    [id, data, graphNodes, edges]
  );

  // Inline validation badge in the node header (first issue; full list on hover).
  const issueBadge = healthIssues.length > 0 ? (
    <span
      title={healthIssues.join('\n')}
      className="shrink-0 max-w-[150px] truncate px-1.5 py-0.5 rounded text-[9px] bg-amber-400/15 text-amber-300 border border-amber-400/40 cursor-help"
    >
      ⚠ {healthIssues[0]}
    </span>
  ) : null;

  // P0: "+" chip in the header → opens the "Add next step" popover anchored to this node.
  const handleAddNextClick = (e: React.MouseEvent<HTMLButtonElement>) => {
    e.stopPropagation();
    const rect = e.currentTarget.getBoundingClientRect();
    openAddNext(id, rect.right, rect.bottom);
  };

  // Read execution state directly from store (avoids parent creating new node objects)
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

  const toggleCollapse = useCallback(() => {
    const next = !collapsed;
    setCollapsed(next);
    updateNodeData(id, { isCollapsed: next });
  }, [collapsed, id, updateNodeData]);

  // ── Collapsed mode ──
  if (collapsed) {
    return (
      <div
        className={`step-node rounded-lg min-w-[180px] ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''} ${executionClass}`}
        style={{
          borderLeft: executionBorderColor ? `3px solid ${executionBorderColor}` : `3px solid ${accentColor}`,
          boxShadow: selected ? `0 0 0 1px ${accentColor}40, 0 4px 12px ${accentColor}20` : 'none',
        }}
      >
        <Handle type="target" position={Position.Left} style={{ background: accentColor, width: 20, height: 20 }} />
        <div className="px-3 py-1.5 flex items-center gap-2 cursor-pointer select-none" onClick={toggleCollapse}>
          <button className="p-0.5 rounded hover:bg-white/10 transition-colors shrink-0">
            <ChevronRight className="w-3.5 h-3.5 text-gray-400" />
          </button>
          <div
            className="w-4 h-4 rounded flex items-center justify-center shrink-0"
            style={{ backgroundColor: `${accentColor}40` }}
          >
            <span className="text-[10px]">{schema?.icon || '📦'}</span>
          </div>
          <span className="text-xs font-medium text-gray-200 truncate flex-1">{data.label || 'Step'}</span>
          {disabled && <AlertCircle className="w-3 h-3 text-amber-400 shrink-0" />}
          {issueBadge}
          {headerExtras}
        </div>
        <Handle type="source" position={Position.Right} style={{ background: accentColor, width: 20, height: 20 }} />
      </div>
    );
  }

  // ── Expanded mode ──
  return (
    <div
      className={`group step-node min-w-[240px] ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''} ${executionClass}`}
      style={{
        border: `1px solid ${executionBorderColor || (selected ? accentColor : `${accentColor}50`)}`,
        boxShadow: selected ? `0 0 0 1px ${accentColor}30, 0 8px 24px ${accentColor}15` : 'none',
      }}
    >
      <div className="flex">
        {/* ── Input Handles (left column) ── */}
        <div className="flex flex-col py-2 pr-2" style={{ minWidth: '90px' }}>
          {inputs.map((input) => (
            <div key={input.id} className="relative flex items-center mb-1">
              <Handle
                id={input.id}
                type="target"
                position={Position.Left}
                style={{
                  background: accentColor,
                  borderColor: accentColor,
                  width: HANDLE_SIZES[input.type],
                  height: HANDLE_SIZES[input.type],
                  right: 'auto',
                  left: '-8px',
                }}
                className="transition-all duration-150 hover:brightness-125 hover:shadow-[0_0_0_4px_rgba(255,255,255,0.18)]"
              />
              <HandleLabel label={input.label} type={input.type} optional={input.optional} side="left" />
            </div>
          ))}
        </div>

        {/* ── Node Body ── */}
        <div className="flex-1 flex flex-col">
          {/* Header */}
          <div
            className="flex items-center gap-2 px-3 py-2 cursor-grab active:cursor-grabbing select-none rounded-t-xl"
            style={{
              background: `linear-gradient(135deg, ${accentColor}22, ${accentColor}11)`,
              borderBottom: `1px solid ${accentColor}33`,
            }}
          >
            <button
              onClick={toggleCollapse}
              className="p-0.5 rounded hover:bg-white/10 transition-colors shrink-0"
            >
              <ChevronDown className="w-3.5 h-3.5 text-gray-400" />
            </button>
            <div
              className="w-5 h-5 rounded-md flex items-center justify-center shrink-0"
              style={{ backgroundColor: `${accentColor}40` }}
            >
              <span className="text-sm">{schema?.icon || '📦'}</span>
            </div>
            <span className="text-sm font-semibold text-gray-100 truncate flex-1">
              {data.label || 'Step'}
            </span>
            {schema && (
              <span className="text-[9px] text-gray-500 font-mono shrink-0">v{schema.version}</span>
            )}
            {disabled && <AlertCircle className="w-3.5 h-3.5 text-amber-400 shrink-0" />}
            {issueBadge}
            {headerExtras}

            {/* P0: Add next step (revealed on hover; only when the node has outputs) */}
            {outputs.length > 0 && (
              <button
                type="button"
                title="Add next step…"
                aria-label={`Add next step after ${data.label || 'step'}`}
                onClick={handleAddNextClick}
                onPointerDown={(e) => e.stopPropagation()}
                className="p-1 rounded-md text-gray-500 hover:text-indigo-300 hover:bg-white/10 transition-all shrink-0 opacity-0 group-hover:opacity-100 focus-visible:opacity-100 cursor-pointer"
              >
                <Plus className="w-4 h-4" />
              </button>
            )}
          </div>

          {/* Children (category-specific content) */}
          <div className="px-3 py-2">{children}</div>

          {/* Footer */}
          <div
            className="flex items-center gap-2 px-3 py-1.5 text-[10px] text-gray-500 rounded-b-xl"
            style={{
              borderTop: `1px solid ${accentColor}22`,
              background: `${accentColor}08`,
            }}
          >
            <span className="font-mono text-[9px]">{data.schemaId || 'step'}</span>
            {footerExtras}
            <span className="flex-1" />
            {schema?.tags?.slice(0, 2).map((tag: string) => (
              <span
                key={tag}
                className="px-1 py-0.5 rounded text-[9px] shrink-0"
                style={{ background: `${accentColor}15`, color: `${accentColor}cc` }}
              >
                {tag}
              </span>
            ))}
          </div>
        </div>

        {/* ── Output Handles (right column, bottom-aligned) ── */}
        <div className="flex flex-col justify-end py-2 pl-2 items-end" style={{ minWidth: '90px' }}>
          {outputs.map((output) => (
            <div key={output.id} className="relative flex items-center justify-end mb-1">
              <HandleLabel label={output.label} type={output.type} optional={false} side="right" />
              <Handle
                id={output.id}
                type="source"
                position={Position.Right}
                style={{
                  background: accentColor,
                  borderColor: accentColor,
                  width: HANDLE_SIZES[output.type],
                  height: HANDLE_SIZES[output.type],
                  left: 'auto',
                  right: '-8px',
                }}
                className="transition-all duration-150 hover:brightness-125 hover:shadow-[0_0_0_4px_rgba(255,255,255,0.18)]"
              />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════
// Shared Utility Components
// ═══════════════════════════════════════════════════════════

export function ConfigBadge({
  label,
  value,
  accentColor,
}: {
  label: string;
  value: string;
  accentColor: string;
}) {
  return (
    <div className="flex items-center gap-1.5 text-xs text-gray-300">
      <span className="text-gray-500">{label}:</span>
      <span
        className="px-1.5 py-0.5 rounded text-[10px] font-mono"
        style={{ background: `${accentColor}15`, color: `${accentColor}cc` }}
      >
        {value}
      </span>
    </div>
  );
}

export function StatusIndicator({
  status,
}: {
  status: 'idle' | 'running' | 'success' | 'error';
}) {
  const config = {
    idle: { icon: null, color: '#6b7280' },
    running: { icon: <Loader2 className="w-3 h-3 animate-spin" />, color: '#f59e0b' },
    success: { icon: <CheckCircle className="w-3 h-3" />, color: '#22c55e' },
    error: { icon: <X className="w-3 h-3" />, color: '#ef4444' },
  };
  const { icon, color } = config[status];
  if (!icon) return null;
  return <span style={{ color }}>{icon}</span>;
}

export function TruncatedText({
  text,
  maxChars = 60,
}: {
  text: string;
  maxChars?: number;
}) {
  if (text.length <= maxChars) return <span className="text-xs text-gray-400">{text}</span>;
  return (
    <span className="text-xs text-gray-400 truncate block max-w-[200px]">
      {text.slice(0, maxChars)}...
    </span>
  );
}
