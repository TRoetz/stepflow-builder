import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { ArrowRight, Clock, GitBranch, Activity } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Utility Node — Gray gradient, contextual icons
// ═══════════════════════════════════════════════════════════

const UTILITY_ICONS: Record<string, { icon: React.ReactNode; label: string }> = {
  'stepflow:utility:pass': {
    icon: <ArrowRight className="w-3.5 h-3.5" />,
    label: 'Pass Through',
  },
  'stepflow:utility:wait': {
    icon: <Clock className="w-3.5 h-3.5" />,
    label: 'Wait',
  },
  'stepflow:utility:branch': {
    icon: <GitBranch className="w-3.5 h-3.5" />,
    label: 'Branch',
  },
};

export function UtilityNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#6B7280';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const duration = config?.duration ?? 5;
  const durationUnit = (config?.durationUnit as string) || 'seconds';
  const condition = (config?.condition as string) || '';
  const branchMode = (config?.branchMode as string) || 'exclusive';
  const enableLogging = config?.enableLogging as boolean;
  const label = (config?.label as string) || '';

  const utilityConfig = UTILITY_ICONS[data.schemaId as string] || {
    icon: <Activity className="w-3.5 h-3.5" />,
    label: 'Utility',
  };

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_data', label: 'Output', type: 'any', position: 'right' },
  ];

  return (
    <BaseNodeWithHandles
      id={id}
      data={data}
      selected={selected}
      inputs={inputs}
      outputs={outputs}
      headerExtras={
        <div className="flex items-center gap-1.5">
          <span
            className="px-1.5 py-0.5 rounded text-[10px] font-mono"
            style={{
              backgroundColor: `${accentColor}25`,
              color: `${accentColor}cc`,
            }}
          >
            {utilityConfig.label}
          </span>
          <span className="text-gray-400">{utilityConfig.icon}</span>
        </div>
      }
    >
      <div className="space-y-2">
        {/* Wait Duration */}
        {data.schemaId === 'stepflow:utility:wait' && (
          <div className="flex items-center gap-2">
            <Clock className="w-3.5 h-3.5 text-gray-400 shrink-0" />
            <ConfigBadge
              label="Duration"
              value={`${duration} ${durationUnit}`}
              accentColor={accentColor}
            />
          </div>
        )}

        {/* Branch Condition */}
        {data.schemaId === 'stepflow:utility:branch' && (
          <>
            <div className="flex items-center gap-2">
              <GitBranch className="w-3.5 h-3.5 text-gray-400 shrink-0" />
              <ConfigBadge label="Mode" value={branchMode} accentColor={accentColor} />
            </div>
            {condition && (
              <div>
                <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Condition</div>
                <pre className="text-xs text-gray-400 bg-gray-900/50 rounded px-2 py-1.5 overflow-hidden font-mono">
                  <code className="block line-clamp-3 whitespace-pre-wrap break-words">
                    {condition}
                  </code>
                </pre>
              </div>
            )}
          </>
        )}

        {/* Pass Through identity — shows at a glance that data flows straight through */}
        {data.schemaId === 'stepflow:utility:pass' && (
          <div className="flex items-center justify-between gap-1 text-[9px] font-mono bg-gray-900/50 rounded px-2 py-1.5 border border-dashed border-gray-500/40">
            <span className="text-emerald-400/80">IN</span>
            <ArrowRight className="w-3 h-3 text-gray-500 shrink-0" />
            <span className="px-1.5 py-0.5 rounded border border-indigo-500/30 bg-indigo-500/15 text-indigo-300 font-semibold tracking-wider">
              PASS
            </span>
            <ArrowRight className="w-3 h-3 text-gray-500 shrink-0" />
            <span className="text-emerald-400/80">OUT (unchanged)</span>
          </div>
        )}

        {/* Pass Through Label */}
        {data.schemaId === 'stepflow:utility:pass' && label && (
          <ConfigBadge label="Label" value={label} accentColor={accentColor} />
        )}

        {/* Logging */}
        {enableLogging && (
          <div className="text-[10px] text-gray-400/80 bg-gray-400/10 rounded px-2 py-1">
            Logging enabled
          </div>
        )}
      </div>
    </BaseNodeWithHandles>
  );
}
