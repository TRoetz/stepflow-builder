import { NodeData } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import { Brain, Sparkles } from 'lucide-react';

// ═══════════════════════════════════════════════════════════
// AI Node — Purple gradient, brain icon, model badge
// ═══════════════════════════════════════════════════════════

export function AiNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#8B5CF6';
  const config = data.configuration as Record<string, unknown> | undefined;

  // When the node uses the saved local model config, the per-node model
  // field is hidden and the badge must reflect the saved model instead.
  const useSavedConfig = config?.modelSource === 'saved';
  const savedModel = useAiModelConfigStore((s) => s.defaultModel);
  const model =
    useSavedConfig && savedModel ? `${savedModel} (saved)` : ((config?.model as string) || 'gpt-4-turbo');
  const temperature = config?.temperature ?? 0.7;
  const llmService = (config?.llmService as string) || 'azureOpen';
  const maxTokens = config?.maxTokens ?? 2000;
  const outputFormat = (config?.outputFormat as string) || 'text';
  const systemPrompt = (config?.systemPrompt as string) || '';
  const prompt = (config?.prompt as string) || '';

  // Resolve inputs/outputs from schema registry
  const { inputs, outputs } = resolveSchemaIO(data.schemaId as string);

  return (
    <BaseNodeWithHandles
      id={id}
      data={data}
      selected={selected}
      inputs={inputs}
      outputs={outputs}
      headerExtras={
        <div className="flex items-center gap-1">
          <Brain className="w-3 h-3 text-purple-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Model Badge */}
        <div className="flex items-center gap-2">
          <Sparkles className="w-3.5 h-3.5 text-purple-400 shrink-0" />
          <ConfigBadge label="Model" value={model} accentColor={accentColor} />
        </div>

        {/* Temperature & Tokens */}
        <div className="flex items-center gap-3">
          <ConfigBadge label="Temp" value={String(temperature)} accentColor={accentColor} />
          <ConfigBadge label="Tokens" value={String(maxTokens)} accentColor={accentColor} />
        </div>

        {/* LLM Service */}
        <ConfigBadge label="Service" value={llmService} accentColor={accentColor} />

        {/* Output Format */}
        <ConfigBadge label="Format" value={outputFormat} accentColor={accentColor} />

        {/* Prompt Preview */}
        {prompt && (
          <div className="mt-1">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Prompt</div>
            <div className="text-xs text-gray-400 line-clamp-2 leading-relaxed bg-gray-900/50 rounded px-2 py-1">
              {prompt}
            </div>
          </div>
        )}

        {/* System Prompt Preview */}
        {systemPrompt && (
          <div className="mt-1">
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">System</div>
            <div className="text-xs text-gray-400 line-clamp-2 leading-relaxed bg-gray-900/50 rounded px-2 py-1">
              {systemPrompt}
            </div>
          </div>
        )}
      </div>
    </BaseNodeWithHandles>
  );
}

// ── Resolve inputs/outputs from schema registry ──
import { schemaById } from '@schemas/index';

function resolveSchemaIO(schemaId: string) {
  const schema = schemaById.get(schemaId);
  return {
    inputs: schema?.inputs ?? [{ id: 'input_data', label: 'Input Data', type: 'json', optional: true, position: 'left' as const }],
    outputs: schema?.outputs ?? [{ id: 'output_result', label: 'Result', type: 'json', position: 'right' as const }],
  };
}

