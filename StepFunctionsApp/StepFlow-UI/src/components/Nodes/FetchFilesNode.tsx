import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { DownloadCloud, Clock, FolderDown } from 'lucide-react';
import { schemaById } from '@schemas/index';

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Fetch Files Node - Sky gradient, download icon, host badge
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

export function FetchFilesNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#0EA5E9';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const host = (config?.host as string) || '';
  const protocol = ((config?.protocol as string) || 'scp').toUpperCase();
  const sourcePath = (config?.sourcePath as string) || '';
  const timeoutSeconds = (config?.timeoutSeconds as number) ?? 120;

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input', label: 'Input (optional)', type: 'any', optional: true, position: 'left' },
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
          <DownloadCloud className="w-3.5 h-3.5 text-sky-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Host */}
        {host && (
          <div className="flex items-center gap-2">
            <DownloadCloud className="w-3.5 h-3.5 text-sky-400 shrink-0" />
            <ConfigBadge label="Host" value={host} accentColor={accentColor} />
          </div>
        )}

        {/* Protocol */}
        <div className="flex items-center gap-2">
          <FolderDown className="w-3.5 h-3.5 text-sky-400 shrink-0" />
          <ConfigBadge label="Protocol" value={protocol} accentColor={accentColor} />
        </div>

        {/* Source path preview */}
        {sourcePath && (
          <div className="flex items-start gap-2">
            <FolderDown className="w-3.5 h-3.5 text-sky-400 shrink-0 mt-0.5" />
            <div className="min-w-0">
              <div className="text-[10px] text-gray-500 uppercase tracking-wider">Source</div>
              <div className="text-xs text-sky-300/80 font-mono truncate">{sourcePath}</div>
            </div>
          </div>
        )}

        {/* Timeout */}
        <div className="flex items-center gap-2">
          <Clock className="w-3.5 h-3.5 text-sky-400 shrink-0" />
          <ConfigBadge label="Timeout" value={`${timeoutSeconds}s`} accentColor={accentColor} />
        </div>
      </div>
    </BaseNodeWithHandles>
  );
}
