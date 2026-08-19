import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { Scale, ShieldCheck, FileCode } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Rule Node — Amber gradient, scale icon, rule count badge
// ═══════════════════════════════════════════════════════════

export function RuleNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#F59E0B';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const evaluationMode = (config?.evaluationMode as string) || 'single';
  const timeout = config?.timeout ?? 30000;
  const ruleSet = config?.ruleSet as string | undefined;
  const workflowName = (config?.workflowName as string) || '';
  const enableDebug = config?.enableDebug as boolean;

  // Parse rule count from ruleSet
  let ruleCount = 0;
  if (ruleSet) {
    try {
      const parsed = typeof ruleSet === 'string' ? JSON.parse(ruleSet) : ruleSet;
      if (Array.isArray(parsed)) {
        ruleCount = parsed.length;
      } else if (parsed?.rules && Array.isArray(parsed.rules)) {
        ruleCount = parsed.rules.length;
      }
    } catch {
      // not valid JSON, show raw
    }
  }

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_result', label: 'Result', type: 'json', position: 'right' },
    { id: 'output_decision', label: 'Decision', type: 'boolean', position: 'right' },
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
          {ruleCount > 0 && (
            <span
              className="px-1.5 py-0.5 rounded text-[10px] font-mono"
              style={{ background: `${accentColor}25`, color: `${accentColor}cc` }}
            >
              {ruleCount} rules
            </span>
          )}
          <Scale className="w-3.5 h-3.5 text-amber-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Evaluation Mode */}
        <div className="flex items-center gap-2">
          <ShieldCheck className="w-3.5 h-3.5 text-amber-400 shrink-0" />
          <ConfigBadge label="Mode" value={evaluationMode} accentColor={accentColor} />
        </div>

        {/* Timeout */}
        <ConfigBadge label="Timeout" value={`${timeout}ms`} accentColor={accentColor} />

        {/* Workflow Name (for MS RulesEngine) */}
        {workflowName && (
          <div className="flex items-center gap-2">
            <FileCode className="w-3.5 h-3.5 text-amber-400 shrink-0" />
            <ConfigBadge label="Workflow" value={workflowName} accentColor={accentColor} />
          </div>
        )}

        {/* Debug indicator */}
        {enableDebug && (
          <div className="text-[10px] text-amber-400/80 bg-amber-400/10 rounded px-2 py-1">
            Debug mode enabled
          </div>
        )}

        {/* Rule Set Preview */}
        {ruleSet && ruleCount === 0 && (
          <div className="mt-1">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Rule Set</div>
            <pre className="text-[10px] text-gray-400 bg-gray-900/50 rounded px-2 py-1 overflow-hidden">
              <code className="block line-clamp-3">
                {typeof ruleSet === 'string' ? ruleSet : JSON.stringify(ruleSet, null, 2)}
              </code>
            </pre>
          </div>
        )}
      </div>
    </BaseNodeWithHandles>
  );
}
