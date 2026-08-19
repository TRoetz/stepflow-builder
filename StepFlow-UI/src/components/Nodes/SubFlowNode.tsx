import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { FolderOpen, RefreshCw, AlertTriangle, Map } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Sub-Flow Node — Cyan gradient, folder icon, version badge
// ═══════════════════════════════════════════════════════════

export function SubFlowNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#06B6D4';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const targetFlowId = (config?.targetFlowId as string) || '';
  const targetFlowVersion = (config?.targetFlowVersion as string) || 'latest';
  const errorMode = (config?.errorMode as string) || 'propagate';
  const retryCount = (config?.retryCount as number) ?? 0;
  const inputMapping = config?.inputMapping as Record<string, string> | undefined;
  const outputMapping = config?.outputMapping as Record<string, string> | undefined;

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
    { id: 'input_params', label: 'Parameters', type: 'json', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_result', label: 'Result', type: 'json', position: 'right' },
    { id: 'output_status', label: 'Status', type: 'string', position: 'right' },
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
          {targetFlowVersion !== 'latest' && (
            <span
              className="px-1.5 py-0.5 rounded text-[10px] font-mono"
              style={{
                backgroundColor: `${accentColor}25`,
                color: `${accentColor}cc`,
              }}
            >
              v{targetFlowVersion}
            </span>
          )}
          <FolderOpen className="w-3.5 h-3.5 text-cyan-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Target Flow */}
        {targetFlowId && (
          <div className="flex items-center gap-2">
            <FolderOpen className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
            <ConfigBadge label="Target" value={targetFlowId} accentColor={accentColor} />
          </div>
        )}

        {/* Version */}
        <ConfigBadge label="Version" value={targetFlowVersion} accentColor={accentColor} />

        {/* Error Handling */}
        <div className="flex items-center gap-2">
          <AlertTriangle className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
          <ConfigBadge label="Error" value={errorMode} accentColor={accentColor} />
        </div>

        {/* Retry */}
        {retryCount > 0 && (
          <div className="flex items-center gap-2">
            <RefreshCw className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
            <ConfigBadge label="Retries" value={String(retryCount)} accentColor={accentColor} />
          </div>
        )}

        {/* Input Mapping Preview */}
        {inputMapping && Object.keys(inputMapping).length > 0 && (
          <div>
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1 flex items-center gap-1">
              <Map className="w-3 h-3" />
              Input Mapping
            </div>
            <div className="space-y-0.5">
              {Object.entries(inputMapping).slice(0, 3).map(([key, value]) => (
                <div key={key} className="text-[10px] text-cyan-300/70 font-mono">
                  {key} → {value}
                </div>
              ))}
              {Object.keys(inputMapping).length > 3 && (
                <div className="text-[10px] text-gray-500">
                  +{Object.keys(inputMapping).length - 3} more...
                </div>
              )}
            </div>
          </div>
        )}

        {/* Output Mapping Preview */}
        {outputMapping && Object.keys(outputMapping).length > 0 && (
          <div>
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1 flex items-center gap-1">
              <Map className="w-3 h-3" />
              Output Mapping
            </div>
            <div className="space-y-0.5">
              {Object.entries(outputMapping).slice(0, 3).map(([key, value]) => (
                <div key={key} className="text-[10px] text-cyan-300/70 font-mono">
                  {key} → {value}
                </div>
              ))}
              {Object.keys(outputMapping).length > 3 && (
                <div className="text-[10px] text-gray-500">
                  +{Object.keys(outputMapping).length - 3} more...
                </div>
              )}
            </div>
          </div>
        )}

        {/* Nested flow indicator */}
        <div className="text-[10px] text-cyan-400/60 bg-cyan-400/5 rounded px-2 py-1 border border-cyan-400/20">
          Click to edit sub-flow
        </div>
      </div>
    </BaseNodeWithHandles>
  );
}
