import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { Database, Table2, Clock } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// Data Node — Blue gradient, database icon, query preview
// ═══════════════════════════════════════════════════════════

export function DataNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#3B82F6';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const query = (config?.query as string) || '';
  const tableName = (config?.tableName as string) || '';
  const enableCaching = config?.enableCaching as boolean;
  const cacheTTL = config?.cacheTTL ?? 300;
  const commandTimeout = config?.commandTimeout ?? 30000;
  const maxRows = config?.maxRows ?? 1000;
  const operation = (config?.operation as string) || '';
  const entityType = (config?.entityType as string) || '';

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: true, position: 'left' },
    { id: 'input_parameters', label: 'Parameters', type: 'json', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_rows', label: 'Rows', type: 'array', position: 'right' },
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
          {enableCaching && (
            <span
              className="px-1.5 py-0.5 rounded text-[10px] font-mono flex items-center gap-1"
              style={{ background: `${accentColor}25`, color: `${accentColor}cc` }}
            >
              <Clock className="w-2.5 h-2.5" />
              cached
            </span>
          )}
          <Database className="w-3.5 h-3.5 text-blue-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* Query Preview */}
        {query && (
          <div>
            <div className="text-[10px] text-gray-500 uppercase tracking-wider mb-1">Query</div>
            <pre className="text-xs text-blue-300/80 bg-gray-900/50 rounded px-2 py-1.5 overflow-hidden font-mono leading-relaxed">
              <code className="block line-clamp-3 whitespace-pre-wrap break-words">
                {query}
              </code>
            </pre>
          </div>
        )}

        {/* Table Name */}
        {tableName && (
          <div className="flex items-center gap-2">
            <Table2 className="w-3.5 h-3.5 text-blue-400 shrink-0" />
            <ConfigBadge label="Table" value={tableName} accentColor={accentColor} />
          </div>
        )}

        {/* Operation & Entity (for EAV) */}
        {operation && <ConfigBadge label="Operation" value={operation} accentColor={accentColor} />}
        {entityType && <ConfigBadge label="Entity" value={entityType} accentColor={accentColor} />}

        {/* Caching Info */}
        {enableCaching && (
          <ConfigBadge label="Cache TTL" value={`${cacheTTL}s`} accentColor={accentColor} />
        )}

        {/* Timeout / Max Rows */}
        {commandTimeout && <ConfigBadge label="Timeout" value={`${commandTimeout}ms`} accentColor={accentColor} />}
        {maxRows && <ConfigBadge label="Max Rows" value={String(maxRows)} accentColor={accentColor} />}
      </div>
    </BaseNodeWithHandles>
  );
}
