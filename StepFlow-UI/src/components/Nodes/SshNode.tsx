import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { Server, ShieldAlert, Clock, Terminal } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// SSH Node — Amber gradient, server icon, host badge
// ═══════════════════════════════════════════════════════════

export function SshNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#F59E0B';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const host = (config?.host as string) || '';
  const command = (config?.command as string) || '';
  const override = config?.override === true;
  const timeoutSeconds = (config?.timeoutSeconds as number) ?? 30;

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input', label: 'Input (command text)', type: 'string', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output', label: 'Output', type: 'json', position: 'right' },
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
          {host && (
            <span
              className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold"
              style={{
                backgroundColor: `${accentColor}25`,
                color: accentColor,
                border: `1px solid ${accentColor}40`,
              }}
            >
              {host}
            </span>
          )}
          <Server className="w-3.5 h-3.5 text-amber-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Host */}
        {host && (
          <div className="flex items-center gap-2">
            <Server className="w-3.5 h-3.5 text-amber-400 shrink-0" />
            <ConfigBadge label="Host" value={host} accentColor={accentColor} />
          </div>
        )}

        {/* Static command preview */}
        {command && (
          <div className="flex items-start gap-2">
            <Terminal className="w-3.5 h-3.5 text-amber-400 shrink-0 mt-0.5" />
            <div className="min-w-0">
              <div className="text-[10px] text-gray-500 uppercase tracking-wider">Command</div>
              <div className="text-xs text-amber-300/80 font-mono truncate">{command}</div>
            </div>
          </div>
        )}

        {/* Timeout */}
        <div className="flex items-center gap-2">
          <Clock className="w-3.5 h-3.5 text-amber-400 shrink-0" />
          <ConfigBadge label="Timeout" value={`${timeoutSeconds}s`} accentColor={accentColor} />
        </div>

        {/* Override warning */}
        {override && (
          <div className="flex items-center gap-2">
            <ShieldAlert className="w-3.5 h-3.5 text-red-400 shrink-0" />
            <span className="text-[10px] font-bold text-red-400 bg-red-400/10 border border-red-400/40 rounded px-2 py-1">
              OVERRIDE — safety check bypassed
            </span>
          </div>
        )}
      </div>
    </BaseNodeWithHandles>
  );
}
