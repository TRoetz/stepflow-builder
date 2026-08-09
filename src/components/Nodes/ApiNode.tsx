import { NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { BaseNodeWithHandles, ConfigBadge } from './BaseNode';
import { Plug, Clock, Shield, RefreshCw, Globe } from 'lucide-react';
import { schemaById } from '@schemas/index';

// ═══════════════════════════════════════════════════════════
// API Node — Green gradient, plug icon, method badge
// ═══════════════════════════════════════════════════════════

// HTTP method color map
const METHOD_COLORS: Record<string, string> = {
  GET: '#3b82f6',
  POST: '#22c55e',
  PUT: '#f59e0b',
  PATCH: '#8b5cf6',
  DELETE: '#ef4444',
  OPTIONS: '#6b7280',
  HEAD: '#6b7280',
};

export function ApiNode({ id, data, selected }: { id: string; data: NodeData; selected?: boolean }) {
  const accentColor = '#10B981';
  const config = data.configuration as Record<string, unknown> | undefined;
  const schema = schemaById.get(data.schemaId as string);

  const method = ((config?.method as string) || 'GET').toUpperCase();
  const url = (config?.url as string) || '';
  const timeout = config?.timeout ?? 30000;
  const retryCount = (config?.retryCount as number) ?? 0;
  const retryDelay = (config?.retryDelay as number) ?? 1000;
  const authentication = (config?.authentication as string) || 'none';
  const apiId = (config?.apiId as string) || '';
  const endpoint = (config?.endpoint as string) || '';
  const enableCaching = config?.enableCaching as boolean;

  const methodColor = METHOD_COLORS[method] || accentColor;

  const inputs: StepInput[] = schema?.inputs ?? [
    { id: 'input_body', label: 'Request Body', type: 'json', optional: true, position: 'left' },
    { id: 'input_headers', label: 'Headers', type: 'json', optional: true, position: 'left' },
  ];
  const outputs: StepOutput[] = schema?.outputs ?? [
    { id: 'output_response', label: 'Response', type: 'json', position: 'right' },
    { id: 'output_status', label: 'Status', type: 'number', position: 'right' },
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
            className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold"
            style={{
              backgroundColor: `${methodColor}25`,
              color: methodColor,
              border: `1px solid ${methodColor}40`,
            }}
          >
            {method}
          </span>
          <Plug className="w-3.5 h-3.5 text-green-400" />
        </div>
      }
    >
      <div className="space-y-2">
        {/* URL Preview */}
        {url && (
          <div className="flex items-start gap-2">
            <Globe className="w-3.5 h-3.5 text-green-400 shrink-0 mt-0.5" />
            <div className="min-w-0">
              <div className="text-[10px] text-gray-500 uppercase tracking-wider">URL</div>
              <div className="text-xs text-green-300/80 font-mono truncate">
                {url}
              </div>
            </div>
          </div>
        )}

        {/* API ID (for registered API) */}
        {apiId && (
          <ConfigBadge label="API" value={apiId} accentColor={accentColor} />
        )}

        {/* Endpoint */}
        {endpoint && (
          <ConfigBadge label="Endpoint" value={endpoint} accentColor={accentColor} />
        )}

        {/* Authentication */}
        <div className="flex items-center gap-2">
          <Shield className="w-3.5 h-3.5 text-green-400 shrink-0" />
          <ConfigBadge label="Auth" value={authentication} accentColor={accentColor} />
        </div>

        {/* Timeout */}
        <div className="flex items-center gap-2">
          <Clock className="w-3.5 h-3.5 text-green-400 shrink-0" />
          <ConfigBadge label="Timeout" value={`${timeout}ms`} accentColor={accentColor} />
        </div>

        {/* Retry */}
        {retryCount > 0 && (
          <div className="flex items-center gap-2">
            <RefreshCw className="w-3.5 h-3.5 text-green-400 shrink-0" />
            <ConfigBadge label="Retries" value={`${retryCount} × ${retryDelay}ms`} accentColor={accentColor} />
          </div>
        )}

        {/* Caching */}
        {enableCaching && (
          <div className="text-[10px] text-green-400/80 bg-green-400/10 rounded px-2 py-1">
            Response caching enabled
          </div>
        )}
      </div>
    </BaseNodeWithHandles>
  );
}
