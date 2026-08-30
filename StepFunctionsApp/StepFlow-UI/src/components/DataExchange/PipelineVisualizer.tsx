// ============================================================================
// Pipeline Visualizer
// Read-only left→right preview of a Data Exchange profile document:
// source (medium + file) → stages in execution order → action chips with
// type-specific details. Pure render over the live editor JSON text — no
// service calls, updates as the user types, degrades to a hint on invalid
// JSON. Enum values are accepted both numeric (wire format) and by name
// (hand-edited profile docs).
// ============================================================================

import { useMemo } from 'react';
import type { ReactNode } from 'react';
import { ArrowRight, FileText } from 'lucide-react';

// ── Enum label maps (values from DataExchange/Enums.cs) ────────────────────

const ACTION_STAGES: Record<number, string> = { 0: 'DataTreatment', 1: 'PreRouting', 2: 'Routing', 3: 'PostRouting' };
const ACTION_TYPES: Record<number, string> = { 0: 'Logic', 1: 'Transformation', 2: 'EnrichmentLookup', 3: 'Dispatch', 4: 'ExecutePipeline' };
const MEDIUM_TYPES: Record<number, string> = { 0: 'Api', 1: 'ApiOAuth', 2: 'Database', 3: 'File' };
const TRANSFORM_TYPES: Record<number, string> = { 0: 'DirectCopy', 1: 'Combine', 2: 'Multiply', 3: 'SetDefault', 4: 'Trim', 5: 'ToUpper', 6: 'FormatDate' };
const LOOKUP_TYPES: Record<number, string> = { 0: 'Api', 1: 'SqlDatabase' };

type EnumMap = Record<number, string>;

/** Resolve an enum value (number or name string) to its label; null when unknown/absent. */
function enumLabel(map: EnumMap, value: unknown): string | null {
  if (typeof value === 'number') return map[value] ?? null;
  if (typeof value !== 'string' || value.trim() === '') return null;
  const asNumber = Number(value);
  if (!Number.isNaN(asNumber) && map[asNumber]) return map[asNumber];
  for (const name of Object.values(map)) {
    if (name.toLowerCase() === value.trim().toLowerCase()) return name;
  }
  return null;
}

/** Label, or the raw value in parens when unknown; null when absent. */
function labelOrRaw(map: EnumMap, value: unknown): string | null {
  const known = enumLabel(map, value);
  if (known) return known;
  if (value === undefined || value === null) return null;
  return `(${String(value)})`;
}

// ── Defensive JSON accessors (profile docs are free-form hand-edited JSON) ─

/** Resolve an enum value (number or name string) to its numeric index; null when unknown/absent. */
function enumIndex(map: EnumMap, value: unknown): number | null {
  if (typeof value === 'number') return map[value] !== undefined ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const asNumber = Number(value);
    if (!Number.isNaN(asNumber) && map[asNumber] !== undefined) return asNumber;
    for (const [key, name] of Object.entries(map)) {
      if (name.toLowerCase() === value.trim().toLowerCase()) return Number(key);
    }
  }
  return null;
}
type JsonObject = { [key: string]: unknown };

function asObject(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as JsonObject) : null;
}

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function num(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }
  return null;
}

function parseProfileDoc(jsonText: string): JsonObject | null {
  try {
    return asObject(JSON.parse(jsonText));
  } catch {
    return null;
  }
}

/** mediumConfigurationJson is a stringified JSON object on the wire (may be an object in hand-edited docs). */
function parseMediumConfig(value: unknown): JsonObject | null {
  if (typeof value === 'string') {
    try {
      return asObject(JSON.parse(value));
    } catch {
      return null;
    }
  }
  return asObject(value);
}

// ── Small presentational pieces ─────────────────────────────────────────────

function Hint({ text }: { text: string }) {
  return <p className="px-1 py-2 text-[10px] italic text-gray-500">{text}</p>;
}

function Arrow() {
  return (
    <div className="shrink-0 self-center">
      <ArrowRight className="h-3.5 w-3.5 text-gray-600" />
    </div>
  );
}

function Placeholder({ text }: { text: string }) {
  return (
    <div className="w-40 shrink-0 self-center rounded-lg border border-dashed border-gray-700 px-2.5 py-2">
      <span className="text-[10px] italic text-gray-600">{text}</span>
    </div>
  );
}

function DetailLine({ children }: { children: ReactNode }) {
  return <div className="mt-1 text-[9px] leading-tight text-gray-400">{children}</div>;
}

// ── Cards & chips ───────────────────────────────────────────────────────────

function SourceCard({ source }: { source: JsonObject }) {
  const medium = labelOrRaw(MEDIUM_TYPES, source.mediumType);
  const name = str(source.dataSourceName);
  const config = parseMediumConfig(source.mediumConfigurationJson);
  const filePath = config ? str(config.filePath) : null;
  const extraKeys = config ? Object.keys(config).filter((k) => k !== 'filePath').slice(0, 2) : [];

  return (
    <div className="w-44 shrink-0 rounded-lg border border-gray-700 bg-gray-800/60 px-2.5 py-2">
      <div className="mb-1 flex items-center gap-1.5">
        <FileText className="h-3 w-3 shrink-0 text-sky-400" />
        <span className="truncate text-[10px] font-semibold uppercase tracking-wider text-gray-400">{medium ?? 'Source'}</span>
      </div>
      {name ? (
        <div className="truncate font-mono text-[11px] text-gray-200" title={name}>
          {name}
        </div>
      ) : null}
      {filePath ? (
        <div className="mt-1 break-all font-mono text-[10px] leading-tight text-sky-300/80" title={filePath}>
          {filePath}
        </div>
      ) : null}
      {extraKeys.map((k) => (
        <div key={k} className="mt-0.5 truncate font-mono text-[9px] text-gray-500">
          {k}={String(config?.[k])}
        </div>
      ))}
    </div>
  );
}

const ACTION_CHIP_STYLES: Record<number, string> = {
  0: 'border-gray-600/70 bg-gray-800/40', // Logic
  1: 'border-indigo-500/40 bg-indigo-500/10', // Transformation
  2: 'border-amber-500/40 bg-amber-500/10', // EnrichmentLookup
  3: 'border-emerald-500/40 bg-emerald-500/10', // Dispatch
  4: 'border-fuchsia-500/40 bg-fuchsia-500/10', // ExecutePipeline
};

function TransformationDetails({ schemaMap }: { schemaMap: JsonObject | null }) {
  const mappings = schemaMap && Array.isArray(schemaMap.attributeMappings) ? (schemaMap.attributeMappings as unknown[]) : [];
  if (mappings.length === 0) return <DetailLine>no mappings</DetailLine>;
  return (
    <div className="mt-1 space-y-0.5">
      <div className="text-[9px] text-gray-400">
        {mappings.length} mapping{mappings.length === 1 ? '' : 's'}
      </div>
      {mappings.slice(0, 3).map((raw, i) => {
        const m = asObject(raw);
        if (!m) return null;
        const targetAttr = asObject(m.targetAttribute);
        const targetName = targetAttr ? str(targetAttr.attributeName) : null;
        const transform = labelOrRaw(TRANSFORM_TYPES, m.transformType);
        return (
          <div key={i} className="flex items-baseline gap-1 text-[9px] leading-tight">
            <span className="truncate font-mono text-gray-300" title={targetName ?? undefined}>
              {targetName ?? '?'}
            </span>
            {transform ? <span className="shrink-0 text-gray-500">· {transform}</span> : null}
          </div>
        );
      })}
      {mappings.length > 3 ? (
        <div className="text-[9px] text-gray-600">+{mappings.length - 3} more</div>
      ) : null}
    </div>
  );
}

function LookupDetails({ lookup }: { lookup: JsonObject | null }) {
  if (!lookup) return <DetailLine>no lookup configured</DetailLine>;
  const url = str(lookup.lookupEndpoint);
  const typeLabel = labelOrRaw(LOOKUP_TYPES, lookup.type);
  const valueField = str(lookup.valueFieldToReturn);
  return (
    <div className="mt-1 space-y-0.5">
      {url ? (
        <div className="truncate font-mono text-[9px] leading-tight text-gray-300" title={url}>
          {url}
        </div>
      ) : null}
      {typeLabel || valueField ? (
        <div className="flex items-center gap-1 text-[9px] leading-tight text-gray-500">
          {typeLabel ? <span>{typeLabel}</span> : null}
          {valueField ? (
            <>
              {typeLabel ? <span>·</span> : null}
              <span className="truncate font-mono" title={valueField}>
                → {valueField}
              </span>
            </>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function DispatchDetails({ endpoint, parameters }: { endpoint: JsonObject | null; parameters: JsonObject | null }) {
  const url = endpoint ? str(endpoint.actionEndpointURL) ?? str(endpoint.actionEndpointName) : null;
  const method = parameters ? str(parameters.Method) : null;
  const format = parameters ? str(parameters.OutputFormat) : null;
  if (!url && !method && !format) return <DetailLine>no endpoint configured</DetailLine>;
  const meta = [method, format].filter((v): v is string => v !== null);
  return (
    <div className="mt-1 space-y-0.5">
      {url ? (
        <div className="truncate font-mono text-[9px] leading-tight text-gray-300" title={url}>
          {url}
        </div>
      ) : null}
      {meta.length > 0 ? (
        <div className="text-[9px] leading-tight text-gray-500">{meta.join(' · ')}</div>
      ) : null}
    </div>
  );
}

function ActionDetails({ action, type }: { action: JsonObject; type: number | null }) {
  switch (type) {
    case 1:
      return <TransformationDetails schemaMap={asObject(action.schemaMap)} />;
    case 2:
      return <LookupDetails lookup={asObject(action.lookup)} />;
    case 3:
      return <DispatchDetails endpoint={asObject(action.endpoint)} parameters={asObject(action.parameters)} />;
    case 4: {
      const subs = Array.isArray(action.subPipelines) ? action.subPipelines.length : 0;
      return (
        <DetailLine>
          {subs} sub-pipeline{subs === 1 ? '' : 's'}
        </DetailLine>
      );
    }
    default: {
      const rules = Array.isArray(action.actionRules) ? action.actionRules.length : null;
      return <DetailLine>{rules !== null && rules > 0 ? `${rules} rule${rules === 1 ? '' : 's'}` : 'logic'}</DetailLine>;
    }
  }
}

function ActionChip({ action }: { action: JsonObject }) {
  const type = enumIndex(ACTION_TYPES, action.type);
  const style = (type !== null && ACTION_CHIP_STYLES[type]) || ACTION_CHIP_STYLES[0];
  const name = str(action.actionName) ?? 'Action';
  const typeLabel = labelOrRaw(ACTION_TYPES, action.type);

  return (
    <div className={`rounded-md border px-2 py-1.5 ${style}`}>
      <div className="flex items-center justify-between gap-1">
        <span className="truncate text-[10px] font-semibold text-gray-200" title={name}>
          {name}
        </span>
        {typeLabel ? (
          <span className="shrink-0 text-[9px] uppercase tracking-wide text-gray-500">{typeLabel}</span>
        ) : null}
      </div>
      <ActionDetails action={action} type={type} />
    </div>
  );
}

function StageCard({ stage }: { stage: JsonObject }) {
  const label = labelOrRaw(ACTION_STAGES, stage.stageType) ?? 'Stage';
  const order = num(stage.executionOrder);
  const actions = Array.isArray(stage.pipelineStageActions)
    ? (stage.pipelineStageActions as unknown[])
        .map(asObject)
        .filter((a): a is JsonObject => a !== null)
        .sort((a, b) => (num(a.executionOrder) ?? 0) - (num(b.executionOrder) ?? 0))
    : [];

  return (
    <div className="w-52 shrink-0 rounded-lg border border-gray-700 bg-gray-800/40 px-2.5 py-2">
      <div className="mb-1.5 flex items-center justify-between gap-1">
        <span className="truncate text-[10px] font-semibold uppercase tracking-wider text-indigo-300">{label}</span>
        {order !== null ? (
          <span className="shrink-0 rounded bg-gray-700/80 px-1 py-0.5 font-mono text-[9px] text-gray-400">#{order}</span>
        ) : null}
      </div>
      {actions.length === 0 ? (
        <div className="py-1 text-[10px] italic text-gray-600">no actions</div>
      ) : (
        <div className="space-y-1.5">
          {actions.map((stageAction, i) => {
            const action = asObject(stageAction.action);
            return action ? <ActionChip key={i} action={action} /> : null;
          })}
        </div>
      )}
    </div>
  );
}

// ── Component ───────────────────────────────────────────────────────────────

export function PipelineVisualizer({ jsonText }: { jsonText: string }) {
  const doc = useMemo(() => parseProfileDoc(jsonText), [jsonText]);

  if (!doc) return <Hint text="Invalid JSON — pipeline preview unavailable" />;

  const source = asObject(doc.dataSource);
  const stages = Array.isArray((asObject(doc.pipeline) ?? {}).pipelineStages)
    ? ((asObject(doc.pipeline) as JsonObject).pipelineStages as unknown[])
        .map(asObject)
        .filter((s): s is JsonObject => s !== null)
        .sort((a, b) => (num(a.executionOrder) ?? 0) - (num(b.executionOrder) ?? 0))
    : [];

  if (!source && stages.length === 0) return <Hint text="No pipeline defined yet" />;

  return (
    <div className="flex items-stretch gap-1 overflow-x-auto pb-1">
      {source ? <SourceCard source={source} /> : null}
      {stages.length > 0 && source ? <Arrow /> : null}
      {stages.map((stage, i) => (
        <StageCard key={`${num(stage.executionOrder) ?? 'x'}-${i}`} stage={stage} />
      ))}
      {source && stages.length === 0 ? (
        <>
          <Arrow />
          <Placeholder text="No stages defined yet" />
        </>
      ) : null}
    </div>
  );
}
