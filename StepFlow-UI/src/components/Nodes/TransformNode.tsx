import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { Code, Clock, ShieldCheck, Terminal } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Transform Node — Pink gradient, code icon, language badge
// ═══════════════════════════════════════════════════════════

const LANGUAGE_COLORS: Record<string, string> = {
  javascript: '#f7df1e',
  python: '#3572A5',
  powershell: '#5391FE',
  jsonata: '#ec4899',
};

export function TransformNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#EC4899';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const language = (config?.language as string) || 'javascript';
  const script = (config?.script as string) || '';
  const expression = (config?.expression as string) || '';
  const timeout = config?.timeout ?? 30000;
  const enableDebug = config?.enableDebug as boolean;
  const enableSandbox = config?.enableSandbox as boolean;

  const langColor = LANGUAGE_COLORS[language?.toLowerCase()] || accentColor;

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_result', label: 'Result', type: 'json', position: 'right' },
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
              backgroundColor: `${langColor}25`,
              color: langColor,
              border: `1px solid ${langColor}40`,
            }}
          >
            {language}
          </span>
          <Code className="w-3.5 h-3.5 text-pink-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Expression Preview (for JSONata) */}
        {expression && (
          <div>
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Expression</div>
            <pre className="text-xs text-pink-300/80 bg-gray-900/50 rounded px-2 py-1.5 overflow-hidden font-mono">
              <code className="block line-clamp-3 whitespace-pre-wrap break-words">
                {expression}
              </code>
            </pre>
          </div>
        )}

        {/* Script Preview */}
        {script && (
          <div>
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Script</div>
            <pre className="text-xs text-pink-300/80 bg-gray-900/50 rounded px-2 py-1.5 overflow-hidden font-mono">
              <code className="block line-clamp-4 whitespace-pre-wrap break-words">
                {script}
              </code>
            </pre>
          </div>
        )}

        {/* Timeout */}
        <div className="flex items-center gap-2">
          <Clock className="w-3.5 h-3.5 text-pink-400 shrink-0" />
          <ConfigBadge label="Timeout" value={`${timeout}ms`} accentColor={accentColor} />
        </div>

        {/* Sandbox / Debug */}
        <div className="flex items-center gap-2">
          {enableSandbox && (
            <span className="text-[10px] text-pink-400/80 bg-pink-400/10 rounded px-1.5 py-0.5 flex items-center gap-1">
              <ShieldCheck className="w-2.5 h-2.5" />
              sandboxed
            </span>
          )}
          {enableDebug && (
            <span className="text-[10px] text-pink-400/80 bg-pink-400/10 rounded px-1.5 py-0.5 flex items-center gap-1">
              <Terminal className="w-2.5 h-2.5" />
              debug
            </span>
          )}
        </div>
      </div>
    </BaseNodeWithHandles>
  );
}
