// ============================================================================
// Schema Editor — visual editing of Data Exchange profile schemas.
// Works on the same JSON document as the Monaco editor: parses jsonText,
// applies structural mutations, and writes back pretty-printed JSON via onChange.
// Wire format is camelCase with numeric enums (Newtonsoft contract).
// ============================================================================

import { useMemo, useState } from 'react';
import { isRecord } from '@utils/typeGuards';
import { FileInput, Link2, Plus, Trash2, Wand2 } from 'lucide-react';
import type { DataExchangeProfile } from '@services/dataExchangeService';

// -- Wire types (camelCase as serialized by the backend) ---------------------

export interface EntityAttribute {
  entityAttributeId?: number;
  attributeName?: string;
  dataType?: number;
  primaryKey?: boolean;
  displayName?: string;
  description?: string;
  visible?: boolean;
  readOnly?: boolean;
  [key: string]: unknown;
}

export interface AttributeDomain {
  attributeDomainId?: number;
  version?: string | number;
  attributeDomainName?: string;
  description?: string;
  isCurrentVersion?: boolean;
  attributes?: EntityAttribute[];
  [key: string]: unknown;
}

export interface SchemaMapWire {
  schemaMapName?: string;
  sourceSchemaId?: number;
  targetSchemaId?: number;
  attributeMappings?: AttributeMapping[];
  [key: string]: unknown;
}

export interface AttributeMapping {
  targetAttributeId?: number;
  targetAttribute?: EntityAttribute | null;
  transformType?: number;
  mergeStrategy?: number;
  sourceAttributes?: EntityAttribute[];
  parameters?: Record<string, string>;
  [key: string]: unknown;
}

interface ActionWire {
  actionName?: string;
  type?: number;
  schemaMap?: SchemaMapWire | null;
  inputSchema?: AttributeDomain | null;
  actionSchema?: AttributeDomain | null;
  [key: string]: unknown;
}

// -- Enum labels (mirror DataExchange/Enums.cs) ------------------------------

export const DATA_TYPE_LABELS = ['String', 'Boolean', 'Number', 'Date', 'Object', 'Array'];
export const TRANSFORM_TYPE_LABELS = ['DirectCopy', 'Combine', 'Multiply', 'SetDefault', 'Trim', 'ToUpper', 'FormatDate'];
export const MERGE_STRATEGY_LABELS = ['AddNewOnly', 'OverwriteExisting', 'AppendValue'];
const STAGE_LABELS = ['DataTreatment', 'PreRouting', 'Routing', 'PostRouting'];

// -- Sample inference ---------------------------------------------------------

function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else inQuotes = false;
      } else field += ch;
    } else if (ch === '"') {
      inQuotes = true;
    } else if (ch === ',') {
      row.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field);
      field = '';
      rows.push(row);
      row = [];
    } else field += ch;
  }
  if (field.length > 0 || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows.filter((r) => r.some((c) => c.trim() !== ''));
}

function inferDataType(values: string[]): number {
  const vals = values.map((v) => v.trim()).filter((v) => v !== '');
  if (vals.length === 0) return 0;
  if (vals.every((v) => /^-?\d+(\.\d+)?([eE][+-]?\d+)?$/.test(v))) return 2; // Number
  if (vals.every((v) => v === 'true' || v === 'false')) return 1; // Boolean
  if (vals.every((v) => /^\d{4}-\d{2}-\d{2}([T ].*)?$/.test(v))) return 3; // Date
  return 0; // String
}

/** Infer an inbound attribute list from a CSV or JSON sample. */
export function inferAttributesFromSample(sample: string): EntityAttribute[] {
  const trimmed = sample.trim();
  if (!trimmed) return [];
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      const parsed: unknown = JSON.parse(trimmed);
      const items = Array.isArray(parsed)
        ? parsed.filter((x): x is Record<string, unknown> => typeof x === 'object' && x !== null)
        : typeof parsed === 'object' && parsed !== null
          ? [parsed as Record<string, unknown>]
          : [];
      if (items.length > 0) {
        return Object.keys(items[0]).map((name, i) => ({
          entityAttributeId: i + 1,
          attributeName: name,
          dataType: inferDataType(items.map((it) => (it[name] == null ? '' : String(it[name])))),
          visible: true,
        }));
      }
    } catch {
      /* fall through to CSV parsing */
    }
  }
  const rows = parseCsv(trimmed);
  if (rows.length === 0) return [];
  const header = rows[0].map((h) => h.trim());
  const dataRows = rows.slice(1, 21);
  return header.map((name, i) => ({
    entityAttributeId: i + 1,
    attributeName: name,
    dataType: inferDataType(dataRows.map((r) => r[i] ?? '')),
    visible: true,
  }));
}

// -- Document helpers ---------------------------------------------------------

function num(v: unknown): number {
  return typeof v === 'number' && Number.isFinite(v) ? v : 0;
}

export function parseProfile(jsonText: string): DataExchangeProfile | null {
  try {
    const value: unknown = JSON.parse(jsonText);
    if (!isRecord(value)) return null;
    if (typeof value.dataExchangeProfileName !== 'string' || !value.dataExchangeProfileName) return null;
    return value as DataExchangeProfile;
  } catch {
    return null;
  }
}

function getInboundDomain(profile: DataExchangeProfile): AttributeDomain | null {
  const ds = profile.dataSource;
  if (!isRecord(ds)) return null;
  const schema = (ds as Record<string, unknown>).importSchema;
  return isRecord(schema) ? (schema as AttributeDomain) : null;
}

interface TransformRef {
  stageIdx: number; // index in pipeline.pipelineStages (original order)
  actionIdx: number; // index in stage.pipelineStageActions
  stage: Record<string, unknown>;
  action: ActionWire;
}

function getTransformationActions(profile: DataExchangeProfile): TransformRef[] {
  const pipeline = profile.pipeline;
  if (!isRecord(pipeline)) return [];
  const stages = Array.isArray(pipeline.pipelineStages) ? (pipeline.pipelineStages as Record<string, unknown>[]) : [];
  const indexed = stages.map((stage, stageIdx) => ({ stage, stageIdx }));
  indexed.sort((a, b) => num(a.stage.executionOrder) - num(b.stage.executionOrder));
  const out: TransformRef[] = [];
  for (const { stage, stageIdx } of indexed) {
    const psas = Array.isArray(stage.pipelineStageActions) ? (stage.pipelineStageActions as Record<string, unknown>[]) : [];
    psas.forEach((psa, actionIdx) => {
      if (!isRecord(psa.action)) return;
      const action = psa.action as ActionWire;
      if (action.type === 1) out.push({ stageIdx, actionIdx, stage, action });
    });
  }
  return out;
}

function nextAttributeId(attributes: EntityAttribute[]): number {
  let max = 0;
  for (const a of attributes) {
    if (typeof a.entityAttributeId === 'number' && a.entityAttributeId > max) max = a.entityAttributeId;
  }
  return max + 1;
}
/**
 * Add a mapping for every inbound attribute that has no mapping yet (matched by
 * target name or target id). Existing mappings are only extended with missing
 * same-named sources. Idempotent: running it twice yields the same result.
 */
export function autoMapSameNames(mappings: AttributeMapping[], inboundAttributes: EntityAttribute[]): AttributeMapping[] {
  const next = mappings.map((m) => ({ ...m }));
  for (const attr of inboundAttributes) {
    if (!attr.attributeName) continue;
    const existing = next.find(
      (m) => m.targetAttribute?.attributeName === attr.attributeName || m.targetAttributeId === attr.entityAttributeId,
    );
    if (existing) {
      const srcs = existing.sourceAttributes ?? [];
      if (!srcs.some((s) => s.attributeName === attr.attributeName)) {
        existing.sourceAttributes = [...srcs, { ...attr }];
      }
    } else {
      next.push({
        transformType: 0,
        sourceAttributes: [{ ...attr }],
        targetAttribute: { attributeName: attr.attributeName, dataType: attr.dataType },
      });
    }
  }
  return next;
}

// -- Shared input styles ------------------------------------------------------

const cellInput =
  'w-full bg-gray-900/60 border border-gray-800 rounded px-1.5 py-1 text-[11px] text-gray-200 focus:outline-none focus:border-indigo-500/50';
const sectionTitle = 'text-[10px] uppercase tracking-wider font-semibold text-gray-500';

// -- Sub-components -----------------------------------------------------------

function AttributeTable({ attributes, onCommit }: { attributes: EntityAttribute[]; onCommit: (next: EntityAttribute[]) => void }) {
  const update = (i: number, patch: Partial<EntityAttribute>) =>
    onCommit(attributes.map((a, j) => (j === i ? { ...a, ...patch } : a)));
  const remove = (i: number) => onCommit(attributes.filter((_, j) => j !== i));
  const add = () =>
    onCommit([...attributes, { entityAttributeId: nextAttributeId(attributes), attributeName: '', dataType: 0, visible: true }]);

  return (
    <div className="border border-gray-800 rounded-lg overflow-hidden">
      <table className="w-full text-[11px]">
        <thead>
          <tr className="bg-gray-800/60 text-gray-400 uppercase tracking-wider text-[9px]">
            <th className="text-left px-2 py-1.5 w-14">ID</th>
            <th className="text-left px-2 py-1.5">Name</th>
            <th className="text-left px-2 py-1.5 w-28">Type</th>
            <th className="text-left px-2 py-1.5 w-24">Display</th>
            <th className="px-2 py-1.5 w-9 text-center" title="Primary key">PK</th>
            <th className="px-2 py-1.5 w-9 text-center" title="Visible">Vis</th>
            <th className="w-8"></th>
          </tr>
        </thead>
        <tbody>
          {attributes.map((a, i) => (
            <tr key={i} className="border-t border-gray-800/60">
              <td>
                <input
                  value={a.entityAttributeId ?? ''}
                  onChange={(e) => update(i, { entityAttributeId: e.target.value === '' ? undefined : Number(e.target.value) })}
                  className={cellInput}
                />
              </td>
              <td>
                <input
                  value={a.attributeName ?? ''}
                  placeholder="attribute name"
                  onChange={(e) => update(i, { attributeName: e.target.value })}
                  className={cellInput}
                />
              </td>
              <td>
                <select value={a.dataType ?? 0} onChange={(e) => update(i, { dataType: Number(e.target.value) })} className={cellInput}>
                  {DATA_TYPE_LABELS.map((label, v) => (
                    <option key={v} value={v}>{label}</option>
                  ))}
                </select>
              </td>
              <td>
                <input
                  value={a.displayName ?? ''}
                  onChange={(e) => update(i, { displayName: e.target.value || undefined })}
                  className={cellInput}
                />
              </td>
              <td className="text-center">
                <input type="checkbox" checked={!!a.primaryKey} onChange={(e) => update(i, { primaryKey: e.target.checked })} />
              </td>
              <td className="text-center">
                <input type="checkbox" checked={a.visible !== false} onChange={(e) => update(i, { visible: e.target.checked })} />
              </td>
              <td>
                <button onClick={() => remove(i)} title="Remove attribute" className="p-1 text-gray-500 hover:text-red-400 transition-colors">
                  <Trash2 className="w-3 h-3" />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="px-2 py-1.5 border-t border-gray-800/60">
        <button onClick={add} className="flex items-center gap-1 text-[10px] font-semibold text-indigo-300 hover:text-indigo-200 transition-colors">
          <Plus className="w-3 h-3" /> Add attribute
        </button>
      </div>
    </div>
  );
}

function MappingRow({
  mapping,
  inbound,
  onChange,
  onRemove,
}: {
  mapping: AttributeMapping;
  inbound: EntityAttribute[];
  onChange: (next: AttributeMapping) => void;
  onRemove: () => void;
}) {
  const [paramText, setParamText] = useState(() => (mapping.parameters ? JSON.stringify(mapping.parameters) : ''));
  const [paramError, setParamError] = useState<string | null>(null);
  const [extraName, setExtraName] = useState('');

  const sources = mapping.sourceAttributes ?? [];
  const hasSource = (name: string) => sources.some((s) => s.attributeName === name);
  const toggleSource = (attr: EntityAttribute) => {
    if (!attr.attributeName) return;
    const next = hasSource(attr.attributeName)
      ? sources.filter((s) => s.attributeName !== attr.attributeName)
      : [...sources, { ...attr }];
    onChange({ ...mapping, sourceAttributes: next });
  };

  const commitParams = (text: string) => {
    setParamText(text);
    if (!text.trim()) {
      onChange({ ...mapping, parameters: undefined });
      setParamError(null);
      return;
    }
    try {
      const v: unknown = JSON.parse(text);
      if (!isRecord(v)) throw new Error('object expected');
      const str: Record<string, string> = {};
      for (const [k, val] of Object.entries(v)) str[k] = typeof val === 'string' ? val : JSON.stringify(val);
      onChange({ ...mapping, parameters: str });
      setParamError(null);
    } catch {
      setParamError('Invalid JSON object');
    }
  };

  const addExtraSource = () => {
    const name = extraName.trim();
    if (!name || hasSource(name)) return;
    onChange({ ...mapping, sourceAttributes: [...sources, { attributeName: name }] });
    setExtraName('');
  };

  return (
    <div className="border border-gray-800 rounded-lg p-2 space-y-1.5">
      <div className="flex items-center gap-2">
        <span className="text-[9px] uppercase tracking-wider text-gray-500 w-14 shrink-0">Target</span>
        <input
          value={mapping.targetAttribute?.attributeName ?? ''}
          placeholder="target field name"
          onChange={(e) =>
            onChange({ ...mapping, targetAttribute: { ...(mapping.targetAttribute ?? {}), attributeName: e.target.value || undefined } })
          }
          className={`${cellInput} flex-1`}
        />
        <select
          value={mapping.transformType ?? 0}
          onChange={(e) => onChange({ ...mapping, transformType: Number(e.target.value) })}
          className={`${cellInput} w-32 shrink-0`}
        >
          {TRANSFORM_TYPE_LABELS.map((label, v) => (
            <option key={v} value={v}>{label}</option>
          ))}
        </select>
        <select
          value={mapping.mergeStrategy ?? 0}
          onChange={(e) => onChange({ ...mapping, mergeStrategy: Number(e.target.value) })}
          className={`${cellInput} w-36 shrink-0`}
          title="Merge strategy"
        >
          {MERGE_STRATEGY_LABELS.map((label, v) => (
            <option key={v} value={v}>{label}</option>
          ))}
        </select>
        <button onClick={onRemove} title="Remove mapping" className="p-1 text-gray-500 hover:text-red-400 transition-colors shrink-0">
          <Trash2 className="w-3 h-3" />
        </button>
      </div>

      <div className="flex items-start gap-2">
        <span className="text-[9px] uppercase tracking-wider text-gray-500 w-14 shrink-0 pt-1.5">Sources</span>
        <div className="flex flex-wrap gap-1 flex-1">
          {inbound.map((attr, i) => {
            const active = hasSource(attr.attributeName ?? '');
            return (
              <button
                key={`${attr.attributeName}-${i}`}
                onClick={() => toggleSource(attr)}
                className={`px-1.5 py-0.5 rounded-md border text-[10px] font-mono transition-colors ${
                  active
                    ? 'bg-indigo-600/30 border-indigo-500/50 text-indigo-200'
                    : 'bg-gray-800/40 border-gray-700/50 text-gray-400 hover:text-gray-200'
                }`}
              >
                {attr.attributeName || `#${i + 1}`}
              </button>
            );
          })}
          <span className="flex items-center gap-1">
            <input
              value={extraName}
              onChange={(e) => setExtraName(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && addExtraSource()}
              placeholder="+ field"
              className={`${cellInput} w-24`}
            />
          </span>
        </div>
      </div>

      <div className="flex items-start gap-2">
        <span className="text-[9px] uppercase tracking-wider text-gray-500 w-14 shrink-0 pt-1.5">Params</span>
        <input
          value={paramText}
          onChange={(e) => commitParams(e.target.value)}
          placeholder='{"separator": " "} — JSON object, optional'
          className={`${cellInput} flex-1 font-mono`}
        />
      </div>
      {paramError && <p className="text-[10px] text-red-400 pl-16">{paramError}</p>}
    </div>
  );
}

// -- Main component -----------------------------------------------------------

export function SchemaEditor({ jsonText, onChange }: { jsonText: string; onChange: (text: string) => void }) {
  const [sampleOpen, setSampleOpen] = useState(false);
  const [sampleText, setSampleText] = useState('');
  const [selectedIdx, setSelectedIdx] = useState(0);

  const profile = useMemo(() => parseProfile(jsonText), [jsonText]);
  const transforms = useMemo(() => (profile ? getTransformationActions(profile) : []), [profile]);
  const inferred = useMemo(() => (sampleOpen ? inferAttributesFromSample(sampleText) : []), [sampleOpen, sampleText]);

  if (!profile) {
    return (
      <div className="h-full flex items-center justify-center px-6">
        <p className="text-xs text-gray-500">Fix the JSON in the editor tab to use schema editing.</p>
      </div>
    );
  }

  const mutate = (fn: (p: DataExchangeProfile) => void) => {
    const p = parseProfile(jsonText);
    if (!p) return;
    fn(p);
    onChange(JSON.stringify(p, null, 2));
  };

  const inbound = getInboundDomain(profile);
  const sel = transforms.length > 0 ? transforms[Math.min(selectedIdx, transforms.length - 1)] : null;

  // -- Inbound schema mutations ----------------------------------------------

  const setInbound = (fn: (d: AttributeDomain) => void) =>
    mutate((p) => {
      const ds = isRecord(p.dataSource) ? p.dataSource : {};
      let domain = getInboundDomain(p);
      if (!domain) domain = { attributeDomainName: 'Inbound Schema', version: '1', isCurrentVersion: true, attributes: [] };
      fn(domain);
      (ds as Record<string, unknown>).importSchema = domain;
      p.dataSource = ds;
    });

  const setInboundAttributes = (next: EntityAttribute[]) => setInbound((d) => { d.attributes = next; });

  // -- Transformation stage mutations -----------------------------------------

  const addTransformationStage = () =>
    mutate((p) => {
      const pipeline = isRecord(p.pipeline) ? p.pipeline : {};
      const stages = Array.isArray(pipeline.pipelineStages) ? (pipeline.pipelineStages as Record<string, unknown>[]) : [];
      const maxOrder = stages.reduce((m, s) => Math.max(m, num(s.executionOrder)), 0);
      stages.push({
        stageType: 0,
        executionOrder: maxOrder + 1,
        pipelineStageActions: [
          {
            executionOrder: 1,
            action: { actionName: `Transform${stages.length + 1}`, type: 1, schemaMap: null },
          },
        ],
      });
      (pipeline as Record<string, unknown>).pipelineStages = stages;
      p.pipeline = pipeline;
    });

  const commitSchemaMap = (next: SchemaMapWire) => {
    if (!sel) return;
    mutate((p) => {
      const pipeline = isRecord(p.pipeline) ? p.pipeline : null;
      const stages = pipeline && Array.isArray(pipeline.pipelineStages) ? (pipeline.pipelineStages as Record<string, unknown>[]) : [];
      const stage = stages[sel.stageIdx];
      if (!stage || !Array.isArray(stage.pipelineStageActions)) return;
      const psa = (stage.pipelineStageActions as Record<string, unknown>[])[sel.actionIdx];
      if (!isRecord(psa?.action)) return;
      (psa.action as ActionWire).schemaMap = next;
    });
  };

  const commitActionSchemaAttributes = (next: EntityAttribute[]) => {
    if (!sel) return;
    mutate((p) => {
      const pipeline = isRecord(p.pipeline) ? p.pipeline : null;
      const stages = pipeline && Array.isArray(pipeline.pipelineStages) ? (pipeline.pipelineStages as Record<string, unknown>[]) : [];
      const stage = stages[sel.stageIdx];
      if (!stage || !Array.isArray(stage.pipelineStageActions)) return;
      const psa = (stage.pipelineStageActions as Record<string, unknown>[])[sel.actionIdx];
      if (!isRecord(psa?.action)) return;
      const action = psa.action as ActionWire;
      let domain: AttributeDomain = isRecord(action.actionSchema) ? (action.actionSchema as AttributeDomain) : { attributeDomainName: 'Output Schema', version: '1', attributes: [] };
      domain.attributes = next;
      action.actionSchema = domain;
    });
  };

  const inboundAttributes = inbound?.attributes ?? [];
  const selMap = sel ? (isRecord(sel.action.schemaMap) ? (sel.action.schemaMap as SchemaMapWire) : null) : null;
  const selOutbound = sel && isRecord(sel.action.actionSchema) ? ((sel.action.actionSchema as AttributeDomain).attributes ?? []) : null;

  return (
    <div className="h-full overflow-y-auto p-3 space-y-4">
      {/* -- Inbound schema ---------------------------------------------------- */}
      <section>
        <div className="flex items-center gap-2 mb-1.5">
          <span className={sectionTitle}>Inbound Schema</span>
          {inbound ? (
            <span className="text-[10px] text-gray-500">{inboundAttributes.length} attributes</span>
          ) : (
            <span className="text-[10px] text-amber-400/80">not attached</span>
          )}
          <div className="ml-auto flex gap-1.5">
            {inbound && (
              <button
                onClick={() => setSampleOpen((v) => !v)}
                className="flex items-center gap-1 px-2 py-0.5 rounded-md border border-gray-700/60 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/10 transition-colors"
              >
                <FileInput className="w-3 h-3" /> Generate from sample
              </button>
            )}
            {!inbound && (
              <button
                onClick={() => setInbound(() => {})}
                className="flex items-center gap-1 px-2 py-0.5 rounded-md border border-gray-700/60 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/10 transition-colors"
              >
                <Link2 className="w-3 h-3" /> Attach schema
              </button>
            )}
          </div>
        </div>

        {sampleOpen && inbound && (
          <div className="space-y-1.5 mb-2">
            <textarea
              value={sampleText}
              onChange={(e) => setSampleText(e.target.value)}
              rows={4}
              placeholder={'Paste a CSV or JSON sample…\nOrderNumber,CustomerId,Quantity\nA-1,9,3'}
              className="w-full bg-gray-900/60 border border-gray-800 rounded-lg p-2 font-mono text-[11px] text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500/50"
            />
            <div className="flex gap-2">
              <button
                onClick={() => {
                  if (inferred.length === 0) return;
                  setInbound((d) => { d.attributes = inferred; });
                  setSampleOpen(false);
                  setSampleText('');
                }}
                disabled={inferred.length === 0}
                className="px-2 py-1 rounded-md bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white text-[10px] font-semibold transition-colors"
              >
                Apply ({inferred.length} attributes)
              </button>
              <button onClick={() => setSampleOpen(false)} className="px-2 py-1 rounded-md border border-gray-700/60 text-[10px] font-semibold text-gray-400 hover:text-gray-200 transition-colors">
                Cancel
              </button>
            </div>
          </div>
        )}

        {inbound && (
          <div className="space-y-1.5">
            <div className="flex gap-2">
              <input
                value={inbound.attributeDomainName ?? ''}
                placeholder="schema name"
                onChange={(e) => setInbound((d) => { d.attributeDomainName = e.target.value || undefined; })}
                className={`${cellInput} w-56`}
              />
              <input
                value={inbound.description ?? ''}
                placeholder="description (optional)"
                onChange={(e) => setInbound((d) => { d.description = e.target.value || undefined; })}
                className={cellInput}
              />
            </div>
            <AttributeTable attributes={inboundAttributes} onCommit={setInboundAttributes} />
          </div>
        )}
      </section>

      {/* -- Transformation actions -------------------------------------------- */}
      <section>
        <div className="flex items-center gap-2 mb-1.5">
          <span className={sectionTitle}>Transformation Actions</span>
          <span className="text-[10px] text-gray-500">{transforms.length}</span>
          <button
            onClick={addTransformationStage}
            className="ml-auto flex items-center gap-1 px-2 py-0.5 rounded-md border border-gray-700/60 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/10 transition-colors"
          >
            <Plus className="w-3 h-3" /> Add stage
          </button>
        </div>

        {transforms.length === 0 ? (
          <p className="text-[11px] text-gray-600">No Transformation actions in the pipeline yet. Add a stage to start mapping.</p>
        ) : (
          <>
            <div className="flex flex-wrap gap-1.5 mb-2">
              {transforms.map((t, i) => (
                <button
                  key={`${t.stageIdx}:${t.actionIdx}`}
                  onClick={() => setSelectedIdx(i)}
                  className={`px-2 py-0.5 rounded-md border text-[10px] font-semibold transition-colors ${
                    i === Math.min(selectedIdx, transforms.length - 1)
                      ? 'bg-indigo-600/30 border-indigo-500/50 text-indigo-200'
                      : 'bg-gray-800/40 border-gray-700/50 text-gray-400 hover:text-gray-200'
                  }`}
                >
                  {STAGE_LABELS[num(t.stage.stageType)] ?? `Stage ${num(t.stage.executionOrder)}`} · {t.action.actionName ?? 'unnamed'}
                </button>
              ))}
            </div>

            {sel && (
              <div className="space-y-2">
                {!selMap ? (
                  <div className="border border-dashed border-gray-700/60 rounded-lg p-3 text-center">
                    <p className="text-[11px] text-gray-500 mb-2">This action has no schema map attached.</p>
                    <button
                      onClick={() => commitSchemaMap({ schemaMapName: 'Schema Map', sourceSchemaId: inbound?.attributeDomainId, attributeMappings: [] })}
                      className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-indigo-600 hover:bg-indigo-500 text-white text-[10px] font-semibold transition-colors"
                    >
                      <Link2 className="w-3 h-3" /> Attach schema map
                    </button>
                  </div>
                ) : (
                  <>
                    <div className="flex items-center gap-2">
                      <input
                        value={selMap.schemaMapName ?? ''}
                        placeholder="schema map name"
                        onChange={(e) => commitSchemaMap({ ...selMap, schemaMapName: e.target.value || undefined })}
                        className={`${cellInput} w-56`}
                      />
                      <button
                        onClick={() => commitSchemaMap({ ...selMap, attributeMappings: autoMapSameNames(selMap.attributeMappings ?? [], inboundAttributes) })}
                        className="flex items-center gap-1 px-2 py-0.5 rounded-md border border-gray-700/60 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/10 transition-colors"
                      >
                        <Wand2 className="w-3 h-3" /> Auto-map same names
                      </button>
                    </div>

                    {(selMap.attributeMappings ?? []).map((m, i) => (
                      <MappingRow
                        key={i}
                        mapping={m}
                        inbound={inboundAttributes}
                        onChange={(next) => commitSchemaMap({ ...selMap, attributeMappings: (selMap.attributeMappings ?? []).map((x, j) => (j === i ? next : x)) })}
                        onRemove={() => commitSchemaMap({ ...selMap, attributeMappings: (selMap.attributeMappings ?? []).filter((_, j) => j !== i) })}
                      />
                    ))}

                    <button
                      onClick={() =>
                        commitSchemaMap({
                          ...selMap,
                          attributeMappings: [...(selMap.attributeMappings ?? []), { transformType: 0, sourceAttributes: [], targetAttribute: undefined }],
                        })
                      }
                      className="flex items-center gap-1 text-[10px] font-semibold text-indigo-300 hover:text-indigo-200 transition-colors"
                    >
                      <Plus className="w-3 h-3" /> Add mapping
                    </button>
                  </>
                )}

                {/* Action output schema */}
                <div className="pt-1">
                  <div className="flex items-center gap-2 mb-1.5">
                    <span className={sectionTitle}>Action Output Schema</span>
                    {selOutbound ? (
                      <span className="text-[10px] text-gray-500">{selOutbound.length} attributes</span>
                    ) : (
                      <button
                        onClick={() => commitActionSchemaAttributes([])}
                        className="flex items-center gap-1 px-2 py-0.5 rounded-md border border-gray-700/60 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/10 transition-colors"
                      >
                        <Link2 className="w-3 h-3" /> Attach output schema
                      </button>
                    )}
                  </div>
                  {selOutbound && <AttributeTable attributes={selOutbound} onCommit={commitActionSchemaAttributes} />}
                </div>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  );
}
