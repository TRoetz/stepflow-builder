// ============================================================================
// Profile Wizard model — pure state ↔ wire-document logic for the Data Exchange
// profile wizard. The wizard state is what the UI edits; buildWireDocument()
// turns it into the backend document (same POCO graph the executor runs, see
// StepFunctionsApp/DataExchange), and profileToWizardState() loads an existing
// document back into wizard state, keeping anything the wizard cannot edit
// (unknown top-level keys, exotic actions) intact on the next save.
// Enums are written as numbers (Newtonsoft ordinals, SchemaEditor convention).
// ============================================================================

import type { DataExchangeProfile } from '@services/dataExchangeService';

// ── Wizard state ────────────────────────────────────────────────────────────

export interface WizardField {
  name: string;
  /** AttributeDataType ordinal (String=0 … Array=5). */
  dataType: number;
}

export interface WizardSchema {
  /** 'existing' = chosen from the attribute-domain catalog; 'defined' = authored here. */
  mode: 'existing' | 'defined';
  name: string;
  fields: WizardField[];
  /** Import only (mode 'defined'): also register the field list as a catalog domain. */
  register: boolean;
}

/** Parameters whose key depends on the transform: separator / defaultValue / format. */
export interface WizardMapping {
  target: string;
  sources: string[];
  /** TransformType ordinal (0 DirectCopy … 6 FormatDate). */
  transform: number;
  /** Value for the transform-specific parameter below; empty = omit. */
  parameter: string;
}

export interface WizardLookup {
  name: string;
  /** Endpoint URL; {Field} tokens are resolved (percent-encoded) per row by the backend. */
  url: string;
  /** Dot-path into the response whose value is merged into the row. */
  valueField: string;
  /** Row key the response value is written to when valueField is absent. */
  outputName: string;
  /** Optional request body / query template (raw {Field} tokens); presence implies POST for GET-less APIs. */
  body: string;
}

export interface WizardDispatch {
  name: string;
  /** http(s):// or file://… target. */
  url: string;
  /** Optional DuckDB-style boolean filter over row columns, e.g. {STATUS} = 'ACTIVE'. */
  filter: string;
  /** Default POST. */
  method: string;
  /** 'csv' | 'json' for file:// targets; empty = backend default. */
  format: string;
  /** Send all rows in one request instead of one per row. */
  batch: boolean;
}

export interface WizardState {
  /** 0-based index into the wizard steps. */
  step: number;
  name: string;
  slug: string;
  isActive: boolean;
  sourceName: string;
  /** DataSourceMediumType ordinal (Api=0, ApiOAuth=1, Database=2, File=3). */
  mediumType: number;
  /** File path inside mediumConfigurationJson (File medium). */
  filePath: string;
  /** Raw extra keys of mediumConfigurationJson for non-File mediums (kept verbatim). */
  extraMediumConfig: Record<string, unknown>;
  importSchema: WizardSchema;
  targetSchema: WizardSchema;
  mappings: WizardMapping[];
  lookups: WizardLookup[];
  dispatches: WizardDispatch[];
  /** Top-level keys of the source document the model does not own — re-emitted on save. */
  extraTop: Record<string, unknown>;
  /** Actions (Logic/ExecutePipeline/…) the wizard does not render — appended on save. */
  extraActions: Array<Record<string, unknown>>;
}

export const STEP_LABELS = ['Basics', 'Source & Import Schema', 'Target Fields', 'Field Mapping', 'Routing & Output', 'Review & Test'] as const;

export const TRANSFORM_OPTIONS = [
  { value: 0, label: 'Direct copy' },
  { value: 1, label: 'Combine sources' },
  { value: 2, label: 'Multiply sources' },
  { value: 3, label: 'Set fixed value' },
  { value: 4, label: 'Trim' },
  { value: 5, label: 'Upper-case' },
  { value: 6, label: 'Format date' },
] as const;

/** Label for the transform-specific parameter input, or null when the transform takes none. */
export function transformParameterLabel(transform: number): string | null {
  switch (transform) {
    case 1: return 'separator';
    case 3: return 'defaultValue';
    case 6: return 'format (e.g. yyyy-MM-dd)';
    default: return null;
  }
}

const TRANSFORM_PARAM_KEYS: Record<number, string> = { 1: 'separator', 3: 'defaultValue', 6: 'format' };

/** True when the transform consumes more than one source field. */
export function isMultiSourceTransform(transform: number): boolean {
  return transform === 1 || transform === 2;
}

export const MEDIUM_OPTIONS = [
  { value: 3, label: 'File (CSV / JSON / XML)' },
  { value: 0, label: 'API' },
  { value: 1, label: 'API (OAuth)' },
  { value: 2, label: 'Database' },
] as const;

/** Slug used for dataexchange:// URIs; mirrors the backend Slug() rules loosely. */
export function slugify(name: string): string {
  return name.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

export function emptyWizardState(): WizardState {
  return {
    step: 0,
    name: '',
    slug: '',
    isActive: true,
    sourceName: '',
    mediumType: 3,
    filePath: '',
    extraMediumConfig: {},
    importSchema: { mode: 'defined', name: '', fields: [], register: false },
    targetSchema: { mode: 'defined', name: '', fields: [], register: false },
    mappings: [],
    lookups: [],
    dispatches: [],
    extraTop: {},
    extraActions: [],
  };
}

// ── Defensive readers (documents are free-form JSON at this boundary) ───────

function rec(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function str(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function bool(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

function num(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

/** Enum values on profile docs come as ordinals or as names (hand-written docs, test templates). */
function enumValue(value: unknown, names: readonly string[], fallback: number): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string') {
    const idx = names.findIndex((n) => n.toLowerCase() === value.trim().toLowerCase());
    if (idx >= 0) return idx;
  }
  return fallback;
}

const TRANSFORM_NAMES = ['DirectCopy', 'Combine', 'Multiply', 'SetDefault', 'Trim', 'ToUpper', 'FormatDate'];
const MEDIUM_NAMES = ['Api', 'ApiOAuth', 'Database', 'File'];

// ── Wire document builders ──────────────────────────────────────────────────

function domainJson(schema: WizardSchema) {
  return {
    attributeDomainName: schema.name.trim(),
    version: '1',
    isCurrentVersion: true,
    attributes: schema.fields
      .filter((f) => f.name.trim())
      .map((f) => ({ attributeName: f.name.trim(), dataType: f.dataType })),
  };
}

function mappingJson(m: WizardMapping) {
  const json: Record<string, unknown> = {
    targetAttribute: { attributeName: m.target.trim() },
    sourceAttributes: m.sources.filter((s) => s.trim()).map((s) => ({ attributeName: s.trim() })),
    transformType: m.transform,
    mergeStrategy: 1, // OverwriteExisting, as in every working profile document
  };
  const key = TRANSFORM_PARAM_KEYS[m.transform];
  if (key && m.parameter !== '') json.parameters = { [key]: m.parameter };
  return json;
}

/** The document exactly as the backend expects it (camelCase, numeric enums). */
export function buildWireDocument(state: WizardState): DataExchangeProfile {
  const slug = state.slug.trim() || slugify(state.name);

  const mediumConfig: Record<string, unknown> = { ...state.extraMediumConfig };
  if (state.filePath.trim()) mediumConfig.filePath = state.filePath.trim();

  const doc: Record<string, unknown> = {
    ...state.extraTop,
    dataExchangeProfileName: state.name.trim(),
    profileId: slug,
    isActive: state.isActive,
    dataSource: {
      dataSourceName: state.sourceName.trim() || state.filePath.trim().split(/[\\/]/).pop() || 'source',
      mediumType: state.mediumType,
      mediumConfigurationJson: JSON.stringify(mediumConfig),
      importSchema: domainJson(state.importSchema),
    },
  };

  const actions: Array<Record<string, unknown>> = [];
  const stageTypeOrders = { base: 1 };
  if (state.mappings.length > 0) {
    actions.push({
      actionName: 'MapToTargetSchema',
      type: 1,
      actionSchema: domainJson(state.targetSchema),
      schemaMap: { attributeMappings: state.mappings.filter((m) => m.target.trim()).map(mappingJson) },
    });
  }
  for (const lookup of state.lookups) {
    const action: Record<string, unknown> = {
      actionName: lookup.name.trim() || 'Lookup',
      type: 2,
      lookup: {
        lookupName: lookup.name.trim(),
        lookupEndpoint: lookup.url.trim(),
        type: 0,
        ...(lookup.valueField.trim() ? { valueFieldToReturn: lookup.valueField.trim() } : {}),
        ...(lookup.body.trim() ? { queryOrBodyTemplate: lookup.body.trim() } : {}),
      },
    };
    if (lookup.outputName.trim()) action.outputParameterName = lookup.outputName.trim();
    actions.push(action);
  }

  const pipelineStages: Array<Record<string, unknown>> = [];
  if (actions.length > 0 || state.extraActions.length > 0) {
    pipelineStages.push({
      stageType: 0, // DataTreatment
      executionOrder: stageTypeOrders.base,
      pipelineStageActions: [...actions, ...state.extraActions].map((action, i) => ({
        executionOrder: i + 1,
        action,
      })),
    });
  }
  if (state.dispatches.length > 0) {
    pipelineStages.push({
      stageType: 1, // PreRouting
      executionOrder: stageTypeOrders.base + 1,
      pipelineStageActions: state.dispatches.map((dispatch, i) => ({
        executionOrder: i + 1,
        action: dispatchActionJson(dispatch),
      })),
    });
  }

  const pipelineDesc = state.targetSchema.name
    ? `Map ${state.importSchema.name || 'source'} fields to ${state.targetSchema.name}, then ${
        pipelineStages.length > 1 ? 'dispatch' : 'process'
      } the result.`
    : '';
  doc.pipeline = {
    pipelineName: slug ? `${slug}-pipeline` : 'pipeline',
    ...(pipelineDesc ? { description: pipelineDesc } : {}),
    pipelineStages,
  };
  return doc as unknown as DataExchangeProfile;
}

// ── Load an existing profile into wizard state ──────────────────────────────
const OWNED_TOP_KEYS: Record<string, true> = {
  dataExchangeProfileName: true, dataExchangeProfileId: true, dataExchangeId: true, dataSourceId: true, pipelineId: true,
  isActive: true, profileId: true, dataSource: true, pipeline: true,
};

function dispatchActionJson(dispatch: WizardDispatch): Record<string, unknown> {
  const parameters: Record<string, string> = {};
  if (dispatch.filter.trim()) parameters.Filter = dispatch.filter.trim();
  if (dispatch.format.trim()) parameters.OutputFormat = dispatch.format.trim().toLowerCase();
  if (dispatch.method.trim() && dispatch.method.trim().toUpperCase() !== 'POST') parameters.Method = dispatch.method.trim().toUpperCase();
  if (dispatch.batch) parameters.Batch = 'true';
  return {
    actionName: dispatch.name.trim() || 'Dispatch',
    type: 3,
    endpoint: { actionEndpointURL: dispatch.url.trim() },
    ...(Object.keys(parameters).length > 0 ? { parameters } : {}),
  };
}
function fieldsFromAttributes(attributes: unknown): WizardField[] {
  if (!Array.isArray(attributes)) return [];
  return attributes
    .map((a) => {
      const r = rec(a);
      return r ? { name: str(r.attributeName), dataType: num(r.dataType, 0) } : null;
    })
    .filter((f): f is WizardField => f !== null && f.name !== '');
}

function schemaFromDomain(domain: unknown, fallbackName: string): WizardSchema {
  const d = rec(domain);
  return {
    mode: 'existing',
    name: d ? str(d.attributeDomainName) : fallbackName,
    fields: fieldsFromAttributes(d?.attributes),
    register: false,
  };
}

function parseMapping(raw: unknown): WizardMapping | null {
  const r = rec(raw);
  if (!r) return null;
  const target = str(rec(r.targetAttribute)?.attributeName);
  const sources = Array.isArray(r.sourceAttributes)
    ? r.sourceAttributes.map((s) => str(rec(s)?.attributeName)).filter((s) => s !== '')
    : [];
  if (!target && sources.length === 0) return null;
  const params = rec(r.parameters);
  const transform = enumValue(r.transformType, TRANSFORM_NAMES, 0);
  const paramKey = TRANSFORM_PARAM_KEYS[transform];
  return {
    target,
    sources,
    transform,
    parameter: paramKey ? str(params?.[paramKey]) : '',
  };
}

/**
 * Loads any profile document into wizard state. Actions the wizard manages
 * (one schema transformation, enrichment lookups, dispatches) become editable;
 * everything else (unknown actions, unknown top-level keys, extra medium config)
 * is preserved and re-emitted by buildWireDocument().
 */
export function profileToWizardState(profile: DataExchangeProfile): WizardState {
  const state = emptyWizardState();
  const doc = profile as Record<string, unknown>;

  for (const [key, value] of Object.entries(doc)) {
    if (!OWNED_TOP_KEYS[key]) state.extraTop[key] = value;
  }

  state.name = str(doc.dataExchangeProfileName);
  state.isActive = bool(doc.isActive, true);
  const rawSlug = str(doc.profileId);
  state.slug = rawSlug && rawSlug !== slugify(state.name) ? rawSlug : '';

  const dataSource = rec(doc.dataSource);
  if (dataSource) {
    state.sourceName = str(dataSource.dataSourceName);
    state.mediumType = enumValue(dataSource.mediumType, MEDIUM_NAMES, 3);
    const rawConfig = dataSource.mediumConfigurationJson;
    const config = typeof rawConfig === 'string' ? rec(safeParse(rawConfig)) : rec(rawConfig);
    if (config) {
      const { filePath, ...rest } = config;
      state.filePath = str(filePath);
      state.extraMediumConfig = rest;
    }
    if (dataSource.importSchema) {
      const domain = rec(dataSource.importSchema);
      state.importSchema = schemaFromDomain(domain, '');
      if (domain) {
        // Loaded inline docs stay 'defined' so their attributes can keep being edited.
        state.importSchema.mode = 'defined';
        state.importSchema.name = str(domain.attributeDomainName);
      }
    }
  }

  const pipeline = rec(doc.pipeline);
  const stages = Array.isArray(pipeline?.pipelineStages) ? (pipeline!.pipelineStages as unknown[]) : [];
  const orderedStages = [...stages]
    .map((s) => ({ stage: rec(s), order: num(rec(s)?.executionOrder, 0) }))
    .sort((a, b) => a.order - b.order);

  const seenActions: unknown[] = [];
  for (const { stage } of orderedStages) {
    const links = Array.isArray(stage?.pipelineStageActions) ? (stage!.pipelineStageActions as unknown[]) : [];
    for (const link of links) seenActions.push(rec(link)?.action ?? null);
  }

  for (const rawAction of seenActions) {
    const action = rec(rawAction);
    if (!action) continue;
    const type = enumValue(action.type, ['Logic', 'Transformation', 'EnrichmentLookup', 'Dispatch', 'ExecutePipeline'], -1);
    if (type === 1 && !state.mappings.length) {
      const map = rec(action.schemaMap);
      const rawMappings = Array.isArray(map?.attributeMappings) ? (map!.attributeMappings as unknown[]) : [];
      const parsed = rawMappings.map(parseMapping).filter((m): m is WizardMapping => m !== null);
      const actionSchema = rec(action.actionSchema);
      const targetFields = fieldsFromAttributes(actionSchema?.attributes);
      const targetName = actionSchema ? str(actionSchema.attributeDomainName) : '';
      state.mappings = parsed;
      if (targetFields.length > 0 || targetName) {
        state.targetSchema = {
          ...schemaFromDomain(actionSchema, ''),
          mode: 'defined',
          name: targetName,
        };
      } else {
        // No explicit target domain: the mapped names are the contract.
        state.targetSchema.fields = parsed
          .filter((m) => m.target)
          .map((m) => ({ name: m.target, dataType: 0 }));
        state.targetSchema.name = 'target';
      }
      continue;
    }
    if (type === 2) {
      const lookup = rec(action.lookup);
      state.lookups.push({
        name: str(action.actionName),
        url: str(lookup?.lookupEndpoint),
        valueField: str(lookup?.valueFieldToReturn),
        outputName: str(action.outputParameterName),
        body: str(lookup?.queryOrBodyTemplate),
      });
      continue;
    }
    if (type === 3) {
      const endpoint = rec(action.endpoint);
      const params = rec(action.parameters);
      state.dispatches.push({
        name: str(action.actionName),
        url: str(endpoint?.actionEndpointURL) || str(endpoint?.actionEndpointUrl),
        filter: str(params?.Filter),
        method: params?.Method ? String(params.Method) : '',
        format: str(params?.OutputFormat),
        batch: String(params?.Batch ?? '').toLowerCase() === 'true',
      });
      continue;
    }
    // Logic / ExecutePipeline / anything else: keep verbatim.
    state.extraActions.push(action as Record<string, unknown>);
  }

  return state;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

// ── Helpers used by the UI ──────────────────────────────────────────────────

/**
 * Ensures every target field has a mapping row and removes rows whose target
 * no longer exists. Rows are matched by target name; existing rows keep their
 * configuration. Returns the new list (never mutates input).
 */
export function syncMappingsWithTarget(mappings: WizardMapping[], targetFields: WizardField[]): WizardMapping[] {
  const names = new Set(targetFields.map((f) => f.name).filter((n) => n !== ''));
  const kept = mappings.filter((m) => names.has(m.target));
  const existingTargets = new Set(kept.map((m) => m.target));
  const added = targetFields
    .filter((f) => f.name && !existingTargets.has(f.name))
    .map((f) => ({ target: f.name, sources: [], transform: 0, parameter: '' }));
  return [...kept, ...added];
}

/**
 * For each mapping without sources, attach the import field with the same name
 * (case-insensitive). Existing source lists are untouched.
 */
export function autoMatchSources(mappings: WizardMapping[], importFields: WizardField[]): WizardMapping[] {
  const lower = new Map(importFields.map((f) => [f.name.toLowerCase(), f.name]));
  return mappings.map((m) => {
    if (m.sources.length > 0 || m.transform === 3) return { ...m };
    const match = lower.get(m.target.toLowerCase());
    return { ...m, sources: match ? [match] : [] };
  });
}

export interface WizardIssue {
  /** Step index the issue belongs to. */
  step: number;
  /** Field-level anchor id shown next to the offending control (optional). */
  anchor?: string;
  message: string;
}

/** Blocking issues per step; empty list at a step means the wizard can advance past it. */
export function validateWizardState(state: WizardState): WizardIssue[] {
  const issues: WizardIssue[] = [];

  if (!state.name.trim()) issues.push({ step: 0, anchor: 'name', message: 'Give the profile a name.' });

  if (state.importSchema.name.trim() === '') issues.push({ step: 1, anchor: 'importName', message: 'Name the import schema (or pick an existing attribute domain).' });
  if (state.importSchema.fields.length === 0) issues.push({ step: 1, anchor: 'importFields', message: 'The import schema needs at least one field — pick a catalog domain or derive fields from a data sample.' });
  if (state.mediumType === 3 && !state.filePath.trim() && !state.extraMediumConfig.filePath) {
    issues.push({ step: 1, anchor: 'filePath', message: 'Set the source file path (or it can be passed at run time).' });
  }

  if (state.targetSchema.name.trim() === '') issues.push({ step: 2, anchor: 'targetName', message: 'Name the target (internal) schema.' });
  if (state.targetSchema.fields.length === 0) issues.push({ step: 2, anchor: 'targetFields', message: 'The target schema needs at least one field.' });

  for (const m of state.mappings) {
    if (!m.target.trim()) issues.push({ step: 3, message: 'Every mapping needs a target field.' });
    else if (m.sources.length === 0 && m.transform !== 3) issues.push({ step: 3, message: `Mapping to “${m.target}” has no source field (or use “Set fixed value”).` });
  }
  const importNames = new Set(state.importSchema.fields.map((f) => f.name.toLowerCase()));
  for (const m of state.mappings) {
    for (const s of m.sources) {
      if (s.trim() && !importNames.has(s.trim().toLowerCase())) {
        issues.push({ step: 3, message: `Source “${s}” is not part of the import schema.` });
      }
    }
  }

  for (const lookup of state.lookups) {
    if (!lookup.url.trim()) issues.push({ step: 4, message: `Enrichment “${lookup.name || 'unnamed'}” needs an endpoint URL.` });
    if (!lookup.valueField.trim() && !lookup.outputName.trim()) {
      issues.push({ step: 4, message: `Enrichment “${lookup.name || 'unnamed'}” needs either a response field to read or an output name.` });
    }
  }
  for (const dispatch of state.dispatches) {
    if (!dispatch.url.trim()) issues.push({ step: 4, message: `Dispatch “${dispatch.name || 'unnamed'}” needs a target URL (http(s):// or file://). ` });
  }

  return issues;
}

export function issuesForStep(issues: WizardIssue[], step: number): WizardIssue[] {
  return issues.filter((i) => i.step === step);
}

/** Catalog registration payload for the attribute-domain registry. */
export function domainRegistryEntry(schema: WizardSchema, description?: string) {
  return {
    schemaDefinition: null,
    attributeDomain: {
      version: '1',
      attributeDomainName: schema.name.trim(),
      description: description?.trim() || null,
      isCurrentVersion: true,
      attributes: schema.fields
        .filter((f) => f.name.trim())
        .map((f) => ({
          attributeName: f.name.trim(),
          dataType: f.dataType,
          description: null,
          displayName: f.name.trim(),
          placeholder: '',
          helpText: '',
          visible: true,
          readOnly: false,
          primaryKey: false,
        })),
    },
  };
}
