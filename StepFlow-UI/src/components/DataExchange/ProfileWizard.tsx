// ============================================================================
// ProfileWizard — guided creation/editing of a Data Exchange profile.
// Replaces "hand-write a giant JSON document" with a 6-step flow:
//   Basics → Source & Import Schema → Target Fields → Field Mapping →
//   Routing & Output (lookups + dispatches) → Review.
// All state ↔ document conversion lives in profileWizardModel.ts; this file is
// presentation + the two service calls (attribute-domain catalog, profile save
// delegated to the parent panel).
// ============================================================================

import { useEffect, useMemo, useState } from 'react';
import { AlertCircle, ArrowRightLeft, Check, ChevronLeft, ChevronRight, Link2, Plus, Send, Trash2, Wand2, X } from 'lucide-react';
import type { DataExchangeProfile } from '@services/dataExchangeService';
import { FormService, type AttributeDomainEntry } from '@services/formService';
import { showToast } from '@stores/useToastStore';
import {
  autoMatchSources,
  buildWireDocument,
  domainRegistryEntry,
  emptyWizardState,
  isMultiSourceTransform,
  issuesForStep,
  MEDIUM_OPTIONS,
  profileToWizardState,
  slugify,
  syncMappingsWithTarget,
  transformParameterLabel,
  validateWizardState,
  TRANSFORM_OPTIONS,
  STEP_LABELS,
  type WizardField,
  type WizardSchema,
  type WizardState,
} from './profileWizardModel';
import { inferAttributesFromSample } from './SchemaEditor';

const DATA_TYPE_LABELS = ['String', 'Boolean', 'Number', 'Date', 'Object', 'Array'];

const inputCls =
  'w-full px-2.5 py-1.5 text-xs rounded-md border border-gray-700 bg-gray-900 text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500';
const labelCls = 'block text-[11px] font-semibold uppercase tracking-wide text-gray-500 mb-1';
const cardCls = 'rounded-lg border border-gray-800 bg-gray-900/60 p-3 space-y-2';

interface ProfileWizardProps {
  open: boolean;
  /** Non-null → edit mode: the document is loaded into the wizard. */
  initial: DataExchangeProfile | null;
  onClose: () => void;
  /** Persist the built document (panel owns DataExchangeService.saveProfile). */
  onSave: (doc: DataExchangeProfile) => Promise<void> | void;
}

export function ProfileWizard({ open, initial, onClose, onSave }: ProfileWizardProps) {
  const [state, setState] = useState<WizardState>(emptyWizardState());
  const [domains, setDomains] = useState<AttributeDomainEntry[]>([]);
  const [extraConfigText, setExtraConfigText] = useState('');
  const [saving, setSaving] = useState(false);

  // (Re)load wizard state whenever the modal opens.
  useEffect(() => {
    if (!open) return;
    const next = initial ? profileToWizardState(initial) : emptyWizardState();
    setState(next);
    setExtraConfigText(JSON.stringify(next.extraMediumConfig, null, 2));
    setSaving(false);
    FormService.listDomains()
      .then(setDomains)
      .catch(() => setDomains([])); // Catalog is optional; 'defined' mode always works
  }, [open, initial]);

  if (!open) return null;

  const issues = validateWizardState(state);
  const stepIssues = issuesForStep(issues, state.step);
  const isEdit = initial !== null;

  const patch = (partial: Partial<WizardState>) => setState((s) => ({ ...s, ...partial }));

  const goNext = () => {
    let next = Math.min(state.step + 1, STEP_LABELS.length - 1);
    if (next === 3) {
      // Entering mapping: ensure one row per target field.
      setState((s) => ({ ...s, step: next, mappings: syncMappingsWithTarget(s.mappings, s.targetSchema.fields) }));
    } else {
      setState((s) => ({ ...s, step: next }));
    }
  };

  const handleSave = async () => {
    const blocking = issues.filter((i) => i.step !== 5);
    if (blocking.length > 0) {
      const worst = Math.min(...blocking.map((i) => i.step));
      setState((s) => ({ ...s, step: worst }));
      showToast({ type: 'error', message: `${blocking.length} issue(s) need fixing before saving` });
      return;
    }
    setSaving(true);
    // Optional catalog registration is best-effort: a failure warns but does not lose the profile.
    const toRegister = [state.importSchema, state.targetSchema].filter((schema) => schema.register);
    for (const schema of toRegister) {
        try {
          await FormService.saveDomain(domainRegistryEntry(schema) as unknown as AttributeDomainEntry);
          showToast({ type: 'success', message: `Attribute domain "${schema.name}" registered` });
        } catch (err) {
          showToast({ type: 'error', message: `Could not register "${schema.name}": ${err instanceof Error ? err.message : err}` });
        }
    }
    try {
      await onSave(buildWireDocument(state));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/70 p-4" onClick={onClose}>
      <div
        className="w-full max-w-5xl h-[92vh] flex flex-col rounded-xl border border-gray-800 bg-gray-950 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-800 shrink-0">
          <Wand2 className="w-4 h-4 text-indigo-400" />
          <div className="text-sm font-semibold text-gray-200">
            {isEdit ? 'Edit profile in wizard' : 'New profile — wizard'}
          </div>
          <div className="flex items-center gap-1 ml-2 overflow-x-auto">
            {STEP_LABELS.map((label, i) => (
              <button
                key={label}
                className={`px-2 py-1 text-[11px] rounded-md whitespace-nowrap transition-colors ${
                  i === state.step
                    ? 'bg-indigo-600 text-white'
                    : i < state.step
                    ? 'text-emerald-400 hover:bg-gray-800'
                    : 'text-gray-500 hover:bg-gray-800'
                }`}
                onClick={() => setState((s) => ({ ...s, step: i }))}
              >
                {i < state.step ? <Check className="inline w-3 h-3 mr-0.5" /> : null}
                {label}
              </button>
            ))}
          </div>
          <button className="ml-auto p-1 text-gray-500 hover:text-gray-300" onClick={onClose} title="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-5 py-4">
          {state.extraActions.length > 0 && (
            <div className="mb-3 flex items-start gap-2 rounded-md border border-amber-700/50 bg-amber-950/30 px-3 py-2 text-xs text-amber-300">
              <AlertCircle className="w-3.5 h-3.5 mt-0.5 shrink-0" />
              <span>
                This document contains {state.extraActions.length} action(s) the wizard does not edit (e.g. Logic).
                They are kept exactly as-is and preserved when you save.
              </span>
            </div>
          )}
          {state.step === 0 && <StepBasics state={state} patch={patch} />}
          {state.step === 1 && (
            <StepImportSchema
              state={state}
              patch={patch}
              setExtraConfigText={setExtraConfigText}
              extraConfigText={extraConfigText}
              domains={domains}
            />
          )}
          {state.step === 2 && <StepTarget state={state} patch={patch} domains={domains} />}
          {state.step === 3 && <StepMapping state={state} setState={setState} />}
          {state.step === 4 && <StepRouting state={state} setState={setState} />}
          {state.step === 5 && <StepReview issues={issues.filter((i) => i.step !== 5)} doc={buildWireDocument(state)} />}
        </div>

        {/* Footer */}
        <div className="px-5 py-3 border-t border-gray-800 shrink-0">
          {stepIssues.length > 0 && (
            <ul className="mb-2 space-y-0.5">
              {stepIssues.map((issue, i) => (
                <li key={i} className="flex items-center gap-1.5 text-[11px] text-rose-400">
                  <AlertCircle className="w-3 h-3 shrink-0" /> {issue.message}
                </li>
              ))}
            </ul>
          )}
          <div className="flex items-center justify-between">
            <button
              className="px-3 py-1.5 text-xs rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 disabled:opacity-40 flex items-center gap-1"
              onClick={() => setState((s) => ({ ...s, step: Math.max(0, s.step - 1) }))}
              disabled={state.step === 0}
            >
              <ChevronLeft className="w-3.5 h-3.5" /> Back
            </button>
            <div className="text-[11px] text-gray-600">
              Step {state.step + 1} / {STEP_LABELS.length}
            </div>
            {state.step < STEP_LABELS.length - 1 ? (
              <button
                className="px-3 py-1.5 text-xs font-semibold rounded-md bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-40 flex items-center gap-1"
                onClick={goNext}
                disabled={stepIssues.length > 0}
              >
                Next <ChevronRight className="w-3.5 h-3.5" />
              </button>
            ) : (
              <button
                className="px-4 py-1.5 text-xs font-semibold rounded-md bg-emerald-600 hover:bg-emerald-500 text-white disabled:opacity-40 flex items-center gap-1"
                onClick={handleSave}
                disabled={saving}
              >
                <Check className="w-3.5 h-3.5" />
                {isEdit ? 'Save changes' : 'Create profile'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Step 1: Basics ──────────────────────────────────────────────────────────

function StepBasics({ state, patch }: { state: WizardState; patch: (p: Partial<WizardState>) => void }) {
  return (
    <div className="space-y-4 max-w-xl">
      <p className="text-xs text-gray-500">
        A profile describes: where rows come from (source), what an incoming row looks like (import schema), how it is
        mapped to your internal contract (target schema + field mappings), and where the result goes (routing & output).
      </p>
      <div>
        <label className={labelCls}>Profile name</label>
        <input className={inputCls} value={state.name} onChange={(e) => patch({ name: e.target.value })} placeholder="e.g. Orders To Store" />
      </div>
      <div>
        <label className={labelCls}>Identifier (used in dataexchange:// URIs)</label>
        <div className="flex items-center gap-2">
          <input
            className={inputCls}
            value={state.slug || slugify(state.name)}
            onChange={(e) => patch({ slug: e.target.value })}
            placeholder={slugify(state.name) || 'auto'}
          />
          {state.slug && (
            <button className="text-[11px] text-gray-500 hover:text-gray-300 whitespace-nowrap" onClick={() => patch({ slug: '' })}>
              auto
            </button>
          )}
        </div>
      </div>
      <label className="flex items-center gap-2 text-xs text-gray-300">
        <input type="checkbox" checked={state.isActive} onChange={(e) => patch({ isActive: e.target.checked })} />
        Active (an inactive profile is skipped when referenced)
      </label>
      <div>
        <label className={labelCls}>Source display name</label>
        <input
          className={inputCls}
          value={state.sourceName}
          onChange={(e) => patch({ sourceName: e.target.value })}
          placeholder="Defaults to the file name"
        />
      </div>
    </div>
  );
}

// ── Shared schema editors ───────────────────────────────────────────────────

function FieldTable({
  fields,
  onChange,
  editable,
}: {
  fields: WizardField[];
  onChange: (next: WizardField[]) => void;
  editable: boolean;
}) {
  return (
    <div className="space-y-1">
      {fields.length === 0 && <div className="text-[11px] text-gray-600">No fields yet.</div>}
      {fields.map((f, i) => (
        <div key={i} className="flex items-center gap-2">
          <input
            className={inputCls + ' flex-1'}
            value={f.name}
            disabled={!editable}
            onChange={(e) => {
              const next = [...fields];
              next[i] = { ...f, name: e.target.value };
              onChange(next);
            }}
          />
          <select
            className="px-2 py-1.5 text-xs rounded-md border border-gray-700 bg-gray-900 text-gray-300 disabled:opacity-60"
            value={f.dataType}
            disabled={!editable}
            onChange={(e) => {
              const next = [...fields];
              next[i] = { ...f, dataType: Number(e.target.value) };
              onChange(next);
            }}
          >
          {DATA_TYPE_LABELS.map((label, idx) => (
            <option key={label} value={idx}>
              {label}
            </option>
          ))}
            </select>
          </div>
        ))}
      </div>
  );
}


// ── Step 2: Source & Import Schema ──────────────────────────────────────────

function StepImportSchema({
  state,
  patch,
  extraConfigText,
  setExtraConfigText,
  domains,
}: {
  state: WizardState;
  patch: (p: Partial<WizardState>) => void;
  extraConfigText: string;
  setExtraConfigText: (t: string) => void;
  domains: AttributeDomainEntry[];
}) {
  const [sample, setSample] = useState('');

  const setImport = (schema: Partial<WizardSchema>) =>
    patch({ importSchema: { ...state.importSchema, ...schema } });


  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4 max-w-2xl">
        <div>
          <label className={labelCls}>Source medium</label>
          <select className={inputCls} value={state.mediumType} onChange={(e) => patch({ mediumType: Number(e.target.value) })}>
            {MEDIUM_OPTIONS.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {state.mediumType === 3 ? (
        <div className="max-w-2xl">
          <label className={labelCls}>File path (CSV / JSON / XML)</label>
          <input
            className={inputCls}
            value={state.filePath}
            onChange={(e) => patch({ filePath: e.target.value })}
            placeholder="data/orders.csv or /absolute/path/orders.json"
          />
          <p className="mt-1 text-[11px] text-gray-600">
            Can be left empty: the run-time caller may pass the path. Extension decides the parser (.csv / .json / .xml).
          </p>
        </div>
      ) : (
        <div className="max-w-2xl">
          <label className={labelCls}>Connection configuration (JSON, stored as-is)</label>
          <textarea
            className={inputCls + ' font-mono min-h-[90px]'}
            value={extraConfigText}
            onChange={(e) => {
              setExtraConfigText(e.target.value);
              try {
                const parsed = JSON.parse(e.target.value);
                if (typeof parsed === 'object' && parsed !== null) {
                  const { filePath, ...rest } = parsed as Record<string, unknown>;
                  patch({ extraMediumConfig: rest, ...(typeof filePath === 'string' ? { filePath } : {}) });
                }
              } catch {
                /* keep raw text until valid */
              }
            }}
          />
        </div>
      )}

      <div className="border-t border-gray-800 pt-3">
        <div className="text-xs font-semibold text-gray-300 mb-2">Import schema (what an incoming row looks like)</div>
        <SchemaSection
          schema={state.importSchema}
          onChange={setImport}
          domains={domains}
          allowRegister
          extra={
            <div className="mt-2">
              <label className={labelCls}>Derive fields from a sample (paste CSV or JSON)</label>
              <div className="flex gap-2">
                <textarea
                  className={inputCls + ' font-mono min-h-[70px] flex-1'}
                  value={sample}
                  onChange={(e) => setSample(e.target.value)}
                  placeholder={'COMPANY_NAME,PRICE,DATE\nACME,10.5,2024-01-01'}
                />
                <button
                  className="px-3 py-1.5 self-start text-xs rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 flex items-center gap-1"
                  onClick={() => {
                    const inferred = inferAttributesFromSample(sample)
                      .map((a) => ({ name: String(a.attributeName ?? ''), dataType: Number(a.dataType) || 0 }))
                      .filter((f) => f.name !== '');
                    if (inferred.length === 0) {
                      showToast({ type: 'error', message: 'Could not read any columns from the sample' });
                      return;
                    }
                    setImport({ fields: inferred });
                    showToast({ type: 'success', message: `${inferred.length} fields derived from the sample` });
                  }}
                >
                  <Wand2 className="w-3.5 h-3.5" /> Derive
                </button>
              </div>
            </div>
          }
        />
      </div>
    </div>
  );
}

// ── Schema section (import + target) ───────────────────────────────────────

function SchemaSection({
  schema,
  onChange,
  domains,
  allowRegister,
  extra,
}: {
  schema: WizardSchema;
  onChange: (next: Partial<WizardSchema>) => void;
  domains: AttributeDomainEntry[];
  allowRegister?: boolean;
  extra?: React.ReactNode;
}) {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3">
        <label className="flex items-center gap-1.5 text-xs text-gray-300">
          <input
            type="radio"
            checked={schema.mode === 'existing'}
            onChange={() => onChange({ mode: 'existing' })}
          />
          Existing attribute domain
        </label>
        <label className="flex items-center gap-1.5 text-xs text-gray-300">
          <input
            type="radio"
            checked={schema.mode === 'defined'}
            onChange={() => onChange({ mode: 'defined' })}
          />
          Define fields here
        </label>
      </div>

      {schema.mode === 'existing' ? (
        <div>
          <label className={labelCls}>Catalog entry</label>
          <select
            className={inputCls}
            value={domains.some((d) => d.attributeDomain.attributeDomainName === schema.name) ? schema.name : ''}
            onChange={(e) => {
              const name = e.target.value;
              const entry = domains.find((d) => d.attributeDomain.attributeDomainName === name);
              onChange({
                name,
                fields: entry
                  ? entry.attributeDomain.attributes.map((a) => ({ name: a.attributeName, dataType: a.dataType }))
                  : [],
              });
            }}
          >
            <option value="">— select —{domains.length === 0 ? ' (catalog empty)' : ''}</option>
            {domains.map((d) => (
              <option key={d.attributeDomain.attributeDomainName} value={d.attributeDomain.attributeDomainName}>
                {d.attributeDomain.attributeDomainName}
              </option>
            ))}
          </select>
        </div>
      ) : (
        <div>
          <label className={labelCls}>Schema name</label>
          <input
            className={inputCls}
            value={schema.name}
            onChange={(e) => onChange({ name: e.target.value })}
            placeholder="e.g. OrderImport"
          />
          {allowRegister && (
            <label className="mt-1.5 flex items-center gap-2 text-[11px] text-gray-400">
              <input type="checkbox" checked={schema.register} onChange={(e) => onChange({ register: e.target.checked })} />
              Also register this field list in the attribute-domain catalog on save
            </label>
          )}
        </div>
      )}

      {extra}

      <div>
        <div className="flex items-center justify-between mb-1">
          <label className={labelCls + ' mb-0'}>Fields {schema.mode === 'existing' ? '(read-only)' : ''}</label>
          {schema.mode === 'defined' && (
            <button
              className="px-2 py-1 text-[11px] rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 flex items-center gap-1"
              onClick={() => onChange({ fields: [...schema.fields, { name: '', dataType: 0 }] })}
            >
              <Plus className="w-3 h-3" /> Add field
            </button>
          )}
        </div>
        <FieldTable
          fields={schema.fields}
          editable={schema.mode === 'defined'}
          onChange={(next) => onChange({ fields: next })}
        />
      </div>
    </div>
  );
}

// ── Step 3: Target Fields ───────────────────────────────────────────────────

function StepTarget({
  state,
  patch,
  domains,
}: {
  state: WizardState;
  patch: (p: Partial<WizardState>) => void;
  domains: AttributeDomainEntry[];
}) {
  const setTarget = (next: Partial<WizardSchema>) => patch({ targetSchema: { ...state.targetSchema, ...next } });
  const copyFromImport = () =>
    setTarget({
      fields: state.importSchema.fields.map((f) => ({ ...f })),
      ...(state.importSchema.name ? { name: `${state.importSchema.name}-mapped` } : {}),
    });

  return (
    <div className="space-y-4">
      <p className="text-xs text-gray-500 max-w-2xl">
        The target schema is the contract everything downstream expects (dispatch payloads, the flow&apos;s Logic step).
        Field names here are what mappings produce.
      </p>
      <SchemaSection
        schema={state.targetSchema}
        onChange={setTarget}
        domains={domains}
        extra={
          <button
            className="px-2.5 py-1 text-[11px] rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300"
            onClick={copyFromImport}
            disabled={state.importSchema.fields.length === 0}
            title="Start from the import field list"
          >
            Copy import fields
          </button>
        }
      />
    </div>
  );
}

// ── Step 4: Field Mapping ───────────────────────────────────────────────────

function StepMapping({
  state,
  setState,
}: {
  state: WizardState;
  setState: (fn: (s: WizardState) => WizardState) => void;
}) {
  const importNames = state.importSchema.fields.map((f) => f.name);
  const patchMapping = (idx: number, partial: Partial<(typeof state.mappings)[number]>) =>
    setState((s) => ({
      ...s,
      mappings: s.mappings.map((m, i) => (i === idx ? { ...m, ...partial } : m)),
    }));

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-xs text-gray-500">
          Each row maps source field(s) into one target field. Direct copy covers most cases; use{' '}
          <span className="text-gray-400">Combine</span>/<span className="text-gray-400">Multiply</span> for several
          sources, <span className="text-gray-400">Set fixed value</span> for constants.
        </p>
        <button
          className="px-2.5 py-1 text-[11px] rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 flex items-center gap-1 shrink-0 ml-3"
          onClick={() => setState((s) => ({ ...s, mappings: autoMatchSources(s.mappings, s.importSchema.fields) }))}
        >
          <ArrowRightLeft className="w-3 h-3" /> Auto-match by name
        </button>
      </div>

      {state.mappings.length === 0 && (
        <div className="text-[11px] text-gray-600">No target fields yet — add them on the previous step.</div>
      )}

      {state.mappings.map((m, idx) => {
        const paramLabel = transformParameterLabel(m.transform);
        const used = new Set(m.sources);
        return (
          <div key={idx} className="flex items-start gap-2 flex-wrap">
            <div className="w-44 shrink-0">
              <div className="text-[11px] font-medium text-gray-300 px-1 py-1.5">{m.target}</div>
            </div>
            <div className="flex-1 min-w-[220px]">
              <div className="flex flex-wrap gap-1 items-center min-h-[30px]">
                {m.sources.map((src) => (
                  <span key={src} className="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-indigo-950 border border-indigo-800 text-[11px] text-indigo-200">
                    {src}
                    <button
                      className="text-indigo-400 hover:text-rose-400"
                      onClick={() => patchMapping(idx, { sources: m.sources.filter((s) => s !== src) })}
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </span>
                ))}
                {m.sources.length === 0 && <span className="text-[11px] text-gray-600">{m.transform === 3 ? 'constant' : 'no source'}</span>}
                <select
                  className="px-1.5 py-1 text-[11px] rounded border border-gray-700 bg-gray-900 text-gray-400"
                  value=""
                  onChange={(e) => {
                    if (!e.target.value) return;
                    patchMapping(idx, { sources: [...m.sources, e.target.value] });
                  }}
                >
                  <option value="">+ source…</option>
                  {(isMultiSourceTransform(m.transform)
                    ? importNames.filter((n) => !used.has(n))
                    : importNames.filter((n) => !used.has(n)).slice(0, importNames.length)
                  ).map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            <div className="flex items-center gap-2 shrink-0">
              <select
                className="px-2 py-1.5 text-xs rounded-md border border-gray-700 bg-gray-900 text-gray-300"
                value={m.transform}
                onChange={(e) => patchMapping(idx, { transform: Number(e.target.value) })}
              >
                {TRANSFORM_OPTIONS.map((t) => (
                  <option key={t.value} value={t.value}>
                    {t.label}
                  </option>
                ))}
              </select>
              {paramLabel && (
                <input
                  className={inputCls + ' w-44'}
                  value={m.parameter}
                  onChange={(e) => patchMapping(idx, { parameter: e.target.value })}
                  placeholder={paramLabel}
                />
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ── Step 5: Routing & Output ────────────────────────────────────────────────

function StepRouting({
  state,
  setState,
}: {
  state: WizardState;
  setState: (fn: (s: WizardState) => WizardState) => void;
}) {
  const patchLookup = (idx: number, partial: Partial<(typeof state.lookups)[number]>) =>
    setState((s) => ({ ...s, lookups: s.lookups.map((l, i) => (i === idx ? { ...l, ...partial } : l)) }));
  const patchDispatch = (idx: number, partial: Partial<(typeof state.dispatches)[number]>) =>
    setState((s) => ({ ...s, dispatches: s.dispatches.map((d, i) => (i === idx ? { ...d, ...partial } : d)) }));

  return (
    <div className="space-y-6">
      <section>
        <div className="flex items-center justify-between mb-2">
          <div className="text-xs font-semibold text-gray-300 flex items-center gap-1.5">
            <Link2 className="w-3.5 h-3.5" /> Enrichment (optional) — call an API per row and merge the response
          </div>
          <button
            className="px-2.5 py-1 text-[11px] rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 flex items-center gap-1"
            onClick={() =>
              setState((s) => ({
                ...s,
                lookups: [...s.lookups, { name: `Lookup${s.lookups.length + 1}`, url: '', valueField: '', outputName: '', body: '' }],
              }))
            }
          >
            <Plus className="w-3 h-3" /> Add enrichment
          </button>
        </div>
        <div className="space-y-3">
          {state.lookups.map((l, idx) => (
            <div key={idx} className={cardCls}>
              <div className="grid grid-cols-4 gap-2">
                <input className={inputCls} value={l.name} placeholder="name" onChange={(e) => patchLookup(idx, { name: e.target.value })} />
                <input
                  className={inputCls + ' col-span-3 font-mono'}
                  value={l.url}
                  placeholder={'https://api…/quote?symbols={COMPANY_NAME}'}
                  onChange={(e) => patchLookup(idx, { url: e.target.value })}
                />
              </div>
              <div className="grid grid-cols-4 gap-2">
                <input
                  className={inputCls + ' col-span-2'}
                  value={l.valueField}
                  placeholder="response field to read (e.g. price)"
                  onChange={(e) => patchLookup(idx, { valueField: e.target.value })}
                />
                <input
                  className={inputCls}
                  value={l.outputName}
                  placeholder="output name"
                  onChange={(e) => patchLookup(idx, { outputName: e.target.value })}
                />
                <button
                  className="px-2 py-1.5 text-xs rounded-md border border-gray-700 text-gray-500 hover:text-rose-400 hover:border-rose-800 justify-self-end"
                  onClick={() => setState((s) => ({ ...s, lookups: s.lookups.filter((_, i) => i !== idx) }))}
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </div>
              <details>
                <summary className="text-[11px] cursor-pointer text-gray-500 hover:text-gray-300">Request body (implies POST)</summary>
                <textarea
                  className={inputCls + ' mt-1 font-mono min-h-[60px]'}
                  value={l.body}
                  placeholder={'{"symbols":["{COMPANY_NAME}"]}'}
                  onChange={(e) => patchLookup(idx, { body: e.target.value })}
                />
              </details>
              <p className="text-[10px] text-gray-600">
                <span className="font-mono">{'{Field}'}</span> tokens in the URL are replaced per row and percent-encoded.
                The value found at the response field is merged into the row (under the field&apos;s own name, or the
                output name if given).
              </p>
            </div>
          ))}
        </div>
      </section>

      <section>
        <div className="flex items-center justify-between mb-2">
          <div className="text-xs font-semibold text-gray-300 flex items-center gap-1.5">
            <Send className="w-3.5 h-3.5" /> Output dispatch — one action per destination
          </div>
          <button
            className="px-2.5 py-1 text-[11px] rounded-md bg-gray-800 hover:bg-gray-700 text-gray-300 flex items-center gap-1"
            onClick={() =>
              setState((s) => ({
                ...s,
                dispatches: [...s.dispatches, { name: `To${s.dispatches.length + 1}`, url: '', filter: '', method: '', format: '', batch: false }],
              }))
            }
          >
            <Plus className="w-3 h-3" /> Add destination
          </button>
        </div>
        <div className="space-y-3">
          {state.dispatches.map((d, idx) => (
            <div key={idx} className={cardCls}>
              <div className="grid grid-cols-6 gap-2">
                <input className={inputCls} value={d.name} placeholder="name" onChange={(e) => patchDispatch(idx, { name: e.target.value })} />
                <input
                  className={inputCls + ' col-span-2 font-mono'}
                  value={d.url}
                  placeholder="https://… or file://out/orders.csv"
                  onChange={(e) => patchDispatch(idx, { url: e.target.value })}
                />
                <select
                  className="px-2 py-1.5 text-xs rounded-md border border-gray-700 bg-gray-900 text-gray-300"
                  value={d.method}
                  onChange={(e) => patchDispatch(idx, { method: e.target.value })}
                >
                  <option value="">POST</option>
                  <option value="GET">GET</option>
                  <option value="PUT">PUT</option>
                  <option value="PATCH">PATCH</option>
                  <option value="DELETE">DELETE</option>
                </select>
                <select
                  className="px-2 py-1.5 text-xs rounded-md border border-gray-700 bg-gray-900 text-gray-300"
                  value={d.format}
                  onChange={(e) => patchDispatch(idx, { format: e.target.value })}
                >
                  <option value="">format: default</option>
                  <option value="csv">csv</option>
                  <option value="json">json</option>
                </select>
                <button
                  className="px-2 py-1.5 text-xs rounded-md border border-gray-700 text-gray-500 hover:text-rose-400 hover:border-rose-800 justify-self-end"
                  onClick={() => setState((s) => ({ ...s, dispatches: s.dispatches.filter((_, i) => i !== idx) }))}
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </div>
              <div className="flex items-center gap-4">
                <input
                  className={inputCls + ' flex-1 font-mono'}
                  value={d.filter}
                  placeholder="optional filter, e.g. {STATUS} = 'ACTIVE'"
                  onChange={(e) => patchDispatch(idx, { filter: e.target.value })}
                />
                <label className="flex items-center gap-1.5 text-[11px] text-gray-400 whitespace-nowrap">
                  <input type="checkbox" checked={d.batch} onChange={(e) => patchDispatch(idx, { batch: e.target.checked })} />
                  batch all rows
                </label>
              </div>
              <p className="text-[10px] text-gray-600">
                Each matching row is sent as one JSON request (or appended to the file). “batch” sends the whole set in a
                single request. Filter uses column names from the mapped target schema.
              </p>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

// ── Step 6: Review ──────────────────────────────────────────────────────────

function StepReview({ issues, doc }: { issues: import('./profileWizardModel').WizardIssue[]; doc: DataExchangeProfile }) {
  const summary = useMemo(() => {
    const d = doc as Record<string, unknown>;
    const pipeline = (d?.pipeline ?? {}) as Record<string, unknown>;
    const stages = Array.isArray(pipeline?.pipelineStages) ? (pipeline.pipelineStages as Array<Record<string, unknown>>) : [];
    const actions = stages.flatMap((s) => (Array.isArray(s?.pipelineStageActions) ? s.pipelineStageActions : []) as Array<Record<string, unknown>>);
    return { actions: actions.length, stages: stages.length, name: String(d?.dataExchangeProfileName ?? ''), profileId: String(d?.profileId ?? '') };
  }, [doc]);

  return (
    <div className="space-y-3">
      {issues.length > 0 ? (
        <div className="rounded-md border border-rose-900 bg-rose-950/30 px-3 py-2">
          <div className="text-xs font-semibold text-rose-300 mb-1">Fix before saving:</div>
          <ul className="space-y-0.5">
            {issues.map((issue, i) => (
              <li key={i} className="text-[11px] text-rose-400 flex items-center gap-1.5">
                <span className="text-gray-500">{STEP_LABELS[issue.step]}:</span> {issue.message}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <div className="rounded-md border border-emerald-900 bg-emerald-950/30 px-3 py-2 text-xs text-emerald-300">
          Ready: {summary.name} · {summary.actions} action(s) across {summary.stages} stage(s) · id <span className="font-mono">{summary.profileId}</span>
        </div>
      )}
      <div>
        <div className="text-[11px] text-gray-500 mb-1">Document that will be saved:</div>
        <pre className="text-[11px] font-mono text-gray-400 bg-gray-950 border border-gray-800 rounded-md p-3 overflow-auto max-h-[40vh]">
          {JSON.stringify(doc, null, 2)}
        </pre>
      </div>
    </div>
  );
}
