import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { ClipboardList, UserCheck } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Form Capture Node — Green accent, JSON form fill-in task
// ═══════════════════════════════════════════════════════════

export function FormCaptureNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#22C55E';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const formId = (config?.formId as string) || '';
  const title = (config?.title as string) || '';
  const assignee = (config?.assignee as string) || '';

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input Data', type: 'any', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'captured_data', label: 'Captured Data', type: 'json', position: 'right' },
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
            FORM
          </span>
          <span className="text-gray-400">
            <ClipboardList className="w-3.5 h-3.5" />
          </span>
        </div>
      }
    >
      <div className="space-y-2">
        {formId && (
          <ConfigBadge label="Form" value={formId} accentColor={accentColor} />
        )}

        {title && (
          <div className="flex items-center gap-2">
            <ClipboardList className="w-3.5 h-3.5 text-gray-400 shrink-0" />
            <ConfigBadge label="Title" value={title} accentColor={accentColor} />
          </div>
        )}

        {assignee && (
          <div className="flex items-center gap-2">
            <UserCheck className="w-3.5 h-3.5 text-gray-400 shrink-0" />
            <ConfigBadge label="Assignee" value={assignee} accentColor={accentColor} />
          </div>
        )}

        {/* Fill-in channel */}
        <div className="text-[10px] text-gray-400/80 bg-gray-900/50 rounded px-2 py-1.5 font-mono">
          /form-capture.html?taskId={'{id}'}
        </div>
      </div>
    </BaseNodeWithHandles>
  );
}
