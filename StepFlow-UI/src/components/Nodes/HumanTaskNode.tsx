import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { FileDown, Globe, UserCheck } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Human Task Node — Orange accent, approval / manual action
// ═══════════════════════════════════════════════════════════

export function HumanTaskNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#FB923C';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const title = (config?.taskTitle as string) || '';
  const assignee = (config?.assignee as string) || '';
  const method = (config?.completionMethod as string) || 'api';
  const watchDirectory = (config?.watchDirectory as string) || '';

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input', type: 'any', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_data', label: 'Completion Result', type: 'json', position: 'right' },
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
            {method === 'file' ? 'FILE DROP' : 'API'}
          </span>
          <span className="text-gray-400">
            {method === 'file' ? (
              <FileDown className="w-3.5 h-3.5" />
            ) : (
              <Globe className="w-3.5 h-3.5" />
            )}
          </span>
        </div>
      }
    >
      <div className="space-y-2">
        {title && (
          <div className="flex items-center gap-2">
            <UserCheck className="w-3.5 h-3.5 text-gray-400 shrink-0" />
            <ConfigBadge label="Title" value={title} accentColor={accentColor} />
          </div>
        )}

        {assignee && (
          <ConfigBadge label="Assignee" value={assignee} accentColor={accentColor} />
        )}

        {/* Completion channel */}
        <div className="text-[10px] text-gray-400/80 bg-gray-900/50 rounded px-2 py-1.5 font-mono">
          {method === 'file' ? (
            <>watch: {watchDirectory || 'human-task-completions'} / {'{taskId}'}.json</>
          ) : (
            <>POST /api/human-tasks/{'{id}'}/complete</>
          )}
        </div>
      </div>
    </BaseNodeWithHandles>
  );
}
