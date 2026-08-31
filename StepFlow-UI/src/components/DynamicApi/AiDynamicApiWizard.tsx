// ============================================================================
// AiDynamicApiWizard — guided AI-assisted dynamic API creation:
//   Phase 1 shell (name/node/basePath/domain/token) →
//   Phase 2 operations (AI-generated per method+intent, always editable) →
//   Phase 3 review + save →
//   Phase 4 test against the engine or a DynamicApiHost URL + deploy panel.
// Reuses the DynamicApiPanel visual language; all calls are same-origin except
// the user-supplied host base URL in phase 4 (DynamicApiHost is CORS-enabled).
// ============================================================================

import { X, Sparkles, Plus, Trash2, Save, Play, Copy, RefreshCw, AlertCircle, Loader2, KeyRound, ChevronLeft, Rocket, CheckCircle2 } from 'lucide-react';
import { DynamicApiService, type HandlerType } from '@services/dynamicApiService';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import { showToast } from '@stores/useToastStore';
import { useCallback, useEffect, useRef, useState } from 'react';
import { suggestOperation, validateOpDraft, AiDraftError, type AiOpContext, type AiOpDraft, type AiApiShell } from '@services/aiDynamicApiBuilder';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const;
const HANDLER_TYPES: HandlerType[] = ['flow', 'attributeDomain', 'eav', 'dataExchange'];

type Phase = 'shell' | 'operations' | 'review' | 'test';
const PHASES: { id: Phase; label: string }[] = [
  { id: 'shell', label: 'Shell' },
  { id: 'operations', label: 'Operations' },
  { id: 'review', label: 'Review & Save' },
  { id: 'test', label: 'Test & Deploy' },
];

/** One operation in the wizard draft (all fields are controlled-input strings). */
interface WizardOp {
  method: string;
  path: string;
  handlerType: HandlerType;
  flowId: string;
  domainName: string;
  profileId: string;
  description: string;
}

const EMPTY_OP: WizardOp = { method: 'GET', path: '', handlerType: 'eav', flowId: '', domainName: '', profileId: '', description: '' };

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** 32 hex chars from crypto.getRandomValues (16 random bytes). */
function generateToken(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    ta.remove();
  }
}

/** Normalize a user-supplied host URL to end in /api/dynamic. */
function normalizeHostUrl(raw: string): string {
  let u = raw.trim().replace(/\/+$/, '');
  if (!u) return '';
  if (!/^https?:\/\//i.test(u)) u = 'http://' + u;
  if (!/\/api\/dynamic$/i.test(u)) u += '/api/dynamic';
  return u;
}

const inputCls = 'w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-indigo-500';
const selectCls = 'bg-gray-800 border border-gray-700 rounded px-1.5 py-1 text-xs text-gray-200 focus:outline-none focus:border-indigo-500';
const labelCls = 'text-[10px] uppercase tracking-wider text-gray-500 font-semibold';

interface AiDynamicApiWizardProps {
  open: boolean;
  initialNodePath: string | null;
  /** First loaded domain name — smart default for the api-level attributeDomain. */
  initialDomain?: string;
  onClose: () => void;
  /** Called after a successful save with the stored id (panel refreshes + loads it). */
  onSaved: (id: string) => void;
}

export function AiDynamicApiWizard({ open, initialNodePath, initialDomain, onClose, onSaved }: AiDynamicApiWizardProps) {
  const hasConfig = useAiModelConfigStore((s) => !!s.baseUrl && !!s.defaultModel);
  const savedModel = useAiModelConfigStore((s) => s.defaultModel);
  const openConfigModal = useAiModelConfigStore((s) => s.openConfigModal);

  // -- Phase + shell draft ----------------------------------------------------
  const [phase, setPhase] = useState<Phase>('shell');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [basePath, setBasePath] = useState('/');
  const [attributeDomain, setAttributeDomain] = useState(initialDomain ?? '');
  const [bearerToken, setBearerToken] = useState('');
  const [isActive, setIsActive] = useState(true);

  // -- Operations -------------------------------------------------------------
  const [ops, setOps] = useState<WizardOp[]>([]);
  const [genMethod, setGenMethod] = useState<string>('GET');
  const [intent, setIntent] = useState('');
  const [generating, setGenerating] = useState(false);
  const [genError, setGenError] = useState('');
  const [opForm, setOpForm] = useState<WizardOp>(EMPTY_OP);
  const [formErrors, setFormErrors] = useState<string[]>([]);

  // -- AI context (fetched same-origin right before each generation call) -----
  const [ctx, setCtx] = useState<AiOpContext | null>(null);
  const [ctxLoading, setCtxLoading] = useState(false);
  const [ctxError, setCtxError] = useState('');

  // -- Review / save ----------------------------------------------------------
  const [published, setPublished] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState('');
  const [savedId, setSavedId] = useState<string | null>(null);

  // -- Test & deploy ----------------------------------------------------------
  const [targetMode, setTargetMode] = useState<'engine' | 'host'>('engine');
  const [hostUrl, setHostUrl] = useState('http://localhost:5002/api/dynamic');
  const [health, setHealth] = useState<{ state: 'idle' | 'checking' | 'ok' | 'error'; detail?: string }>({ state: 'idle' });
  const [testUrls, setTestUrls] = useState<Record<number, string>>({});
  const [testBodies, setTestBodies] = useState<Record<number, string>>({});
  const [testingIdx, setTestingIdx] = useState<number | null>(null);
  const [testResults, setTestResults] = useState<Record<number, { status: number; ms: number; body: string } | null>>({});

  // -- Derived ------------------------------------------------------------------
  const shellForValidation: AiApiShell = {
    basePath: basePath.trim() || '/',
    attributeDomain: attributeDomain.trim() || null,
    existingOps: ops.map((o) => ({ method: o.method, path: o.path, handlerType: o.handlerType, flowId: o.flowId || null, domainName: o.domainName || null, profileId: o.profileId || null, description: o.description || null })),
  };

  const hostBase = targetMode === 'host' ? normalizeHostUrl(hostUrl) : '';
  const targetBase = targetMode === 'engine' ? '/api/dynamic' : hostBase;
  const healthUrl = hostBase ? `${hostBase.replace(/\/api\/dynamic$/i, '')}/health` : '';

  const defaultUrl = useCallback((idx: number): string => {
    const op = ops[idx];
    if (!op) return targetBase;
    return `${targetBase}${DynamicApiService.joinPaths(basePath.trim() || '/', op.path)}`;
  }, [ops, targetBase, basePath]);

  // -- Context loading ----------------------------------------------------------
  const loadContext = useCallback(async (): Promise<AiOpContext | null> => {
    setCtxLoading(true);
    setCtxError('');
    try {
      const [domains, flows, profiles] = await Promise.all([DynamicApiService.domains(), DynamicApiService.flows(), DynamicApiService.profiles()]);
      const sampleRows: Record<string, unknown[]> = {};
      const targetDomain = attributeDomain.trim();
      if (targetDomain) {
        try {
          sampleRows[targetDomain] = await DynamicApiService.eavSampleRows(targetDomain, 3);
        } catch { /* sample rows are optional grounding — non-fatal */ }
      }
      const next: AiOpContext = { domains, flows, profiles, sampleRows };
      setCtx(next);
      return next;
    } catch (err) {
      setCtxError(errorMessage(err));
      return ctx; // fall back to whatever we already have
    } finally {
      setCtxLoading(false);
    }
  }, [attributeDomain, ctx]);

  // Reset all draft state every time the wizard opens (component stays mounted).
  useEffect(() => {
    if (!open) return;
    setPhase('shell');
    setName(''); setDescription(''); setBasePath('/'); setAttributeDomain(initialDomain ?? '');
    setBearerToken(''); setIsActive(true);
    setOps([]); setGenMethod('GET'); setIntent(''); setOpForm({ ...EMPTY_OP }); setFormErrors([]);
    setCtx(null); setCtxLoading(false); setCtxError('');
    setPublished(false); setSaving(false); setSaveError(''); setSavedId(null);
    setTargetMode('engine'); setHostUrl('http://localhost:5002/api/dynamic');
    setHealth({ state: 'idle' }); setTestUrls({}); setTestBodies({}); setTestResults({}); setTestingIdx(null);
    healthAutoKeyRef.current = null;
  }, [open, initialDomain]);

  useEffect(() => {
    if (open && phase === 'operations' && !ctx) void loadContext();
  }, [open, phase, ctx, loadContext]);

  // -- AI generation (max 2 attempts: initial + one retry with error feedback) ---
  const handleGenerate = useCallback(async () => {
    setGenError('');
    if (!hasConfig) {
      showToast({ type: 'error', message: 'No AI model configured — open Settings → AI Model first.' });
      openConfigModal();
      return;
    }
    if (!intent.trim()) {
      setGenError('Describe what the operation should do first.');
      return;
    }
    const config = useAiModelConfigStore.getState();
    setGenerating(true);
    try {
      const freshCtx = (await loadContext()) ?? ctx;
      if (!freshCtx) throw new Error(ctxError || 'Could not load workspace context for the AI.');

      let draft: AiOpDraft | null = null;
      let lastError = '';
      for (let attempt = 0; attempt < 2 && !draft; attempt++) {
        try {
          draft = await suggestOperation(config, genMethod, intent.trim(), freshCtx, shellForValidation, attempt === 1 ? lastError : undefined);
        } catch (err) {
          lastError = errorMessage(err);
          if (err instanceof AiDraftError && err.draft) draft = null; // keep retrying with feedback
        }
      }

      if (!draft) throw new Error(`${lastError} — edit the form manually below.`);

      setOpForm({ method: draft.method || genMethod, path: draft.path ?? '', handlerType: draft.handlerType, flowId: draft.flowId ?? '', domainName: draft.domainName ?? '', profileId: draft.profileId ?? '', description: draft.description ?? '' });
      setFormErrors([]);
    } catch (err) {
      // Prefill with the best-effort parse when one exists so the user can fix it in place.
      if (err instanceof AiDraftError && err.draft) {
        const d = err.draft;
        setOpForm({ method: d.method || genMethod, path: d.path ?? '', handlerType: d.handlerType, flowId: d.flowId ?? '', domainName: d.domainName ?? '', profileId: d.profileId ?? '', description: d.description ?? '' });
      }
      setGenError(errorMessage(err));
    } finally {
      setGenerating(false);
    }
  }, [hasConfig, openConfigModal, intent, ctx, ctxError, loadContext, genMethod, shellForValidation]);

  // -- Op form -> ops list --------------------------------------------------------
  const addOpToOps = useCallback(() => {
    const errors = validateOpDraft(
      { method: opForm.method, path: opForm.path, handlerType: opForm.handlerType, flowId: opForm.flowId || null, domainName: opForm.domainName || null, profileId: opForm.profileId || null, description: opForm.description || null },
      shellForValidation,
    );
    if (errors.length > 0) {
      setFormErrors(errors);
      return;
    }
    setOps((prev) => [...prev, { ...opForm }]);
    setOpForm(EMPTY_OP);
    setFormErrors([]);
    setGenError('');
  }, [opForm, shellForValidation]);

  // -- Save ------------------------------------------------------------------------
  const buildPayload = useCallback(() => ({
    ...(savedId ? { id: savedId } : {}),
    name: name.trim(),
    description: description.trim() || null,
    nodePath: initialNodePath ?? '',
    basePath: basePath.trim() || '/',
    attributeDomain: attributeDomain.trim() || null,
    bearerToken: bearerToken.trim() || null,
    isActive,
    isPublished: published,
    operations: ops.map((o) => ({ method: o.method.toUpperCase(), path: o.path, handlerType: o.handlerType, flowId: o.flowId || null, domainName: o.domainName || null, profileId: o.profileId || null, description: o.description || null })),
  }), [savedId, name, description, initialNodePath, basePath, attributeDomain, bearerToken, isActive, published, ops]);

  const saveApi = useCallback(async () => {
    setSaveError('');
    if (!name.trim()) {
      setSaveError('Name is required.');
      return;
    }
    if (ops.length === 0) {
      setSaveError('Add at least one operation first.');
      return;
    }
    const allErrors: string[] = [];
    ops.forEach((o, i) => {
      const others = ops.filter((_, j) => j !== i).map((x) => ({ method: x.method, path: x.path, handlerType: x.handlerType, flowId: x.flowId || null, domainName: x.domainName || null, profileId: x.profileId || null, description: x.description || null }));
      const errs = validateOpDraft({ method: o.method, path: o.path, handlerType: o.handlerType, flowId: o.flowId || null, domainName: o.domainName || null, profileId: o.profileId || null, description: o.description || null }, { basePath: shellForValidation.basePath, attributeDomain: shellForValidation.attributeDomain, existingOps: others });
      allErrors.push(...errs.map((e) => `op ${i + 1} (${o.method} ${DynamicApiService.joinPaths(shellForValidation.basePath, o.path)}): ${e}`));
    });
    if (allErrors.length > 0) {
      setSaveError(allErrors.join('\n'));
      return;
    }

    setSaving(true);
    try {
      const result = await DynamicApiService.save(buildPayload());
      setSavedId(result.id);
      showToast({ type: 'success', message: `Dynamic API "${name.trim()}" saved.` });
      onSaved(result.id);
      setPhase('test');
    } catch (err) {
      setSaveError(errorMessage(err)); // server errors shown verbatim (e.g. 409 route conflict)
    } finally {
      setSaving(false);
    }
  }, [name, ops, shellForValidation, buildPayload, onSaved]);

  // -- Test & deploy ------------------------------------------------------------------
  const runHealthCheck = useCallback(async () => {
    if (!healthUrl) return;
    setHealth({ state: 'checking' });
    try {
      const res = await fetch(healthUrl);
      const body = (await res.json().catch(() => null)) as { status?: string; apisLoaded?: number } | null;
      setHealth(res.ok ? { state: 'ok', detail: `ready · ${body?.apisLoaded ?? 0} api(s) loaded` } : { state: 'error', detail: `HTTP ${res.status}` });
    } catch (err) {
      setHealth({ state: 'error', detail: errorMessage(err) });
    }
  }, [healthUrl]);

  // Prefill test URLs whenever the target or op set changes.
  useEffect(() => {
    if (!open || phase !== 'test') return;
    setTestUrls(Object.fromEntries(ops.map((_, i) => [i, defaultUrl(i)])));
  }, [open, phase, targetMode, hostUrl, ops.length, defaultUrl]);

  // Auto health check once per entry into the test phase in host mode (manual button re-checks).
  const healthAutoKeyRef = useRef<string | null>(null);
  useEffect(() => {
    if (!open || phase !== 'test' || targetMode !== 'host') return;
    const key = `${phase}|${targetMode}`;
    if (healthAutoKeyRef.current === key) return;
    healthAutoKeyRef.current = key;
    void runHealthCheck();
  }, [open, phase, targetMode, runHealthCheck]);

  const sendTest = useCallback(async (idx: number) => {
    const op = ops[idx];
    if (!op) return;
    const url = testUrls[idx] ?? defaultUrl(idx);
    setTestingIdx(idx);
    setTestResults((r) => ({ ...r, [idx]: null }));
    try {
      let body: string | undefined;
      const rawBody = (testBodies[idx] ?? '').trim();
      if (op.method !== 'GET' && op.method !== 'DELETE' && rawBody) {
        JSON.parse(rawBody); // validate before sending — throws with a readable message
        body = rawBody;
      }
      const headers: Record<string, string> = {};
      if (body) headers['Content-Type'] = 'application/json';
      const token = bearerToken.trim();
      if (token) headers.Authorization = `Bearer ${token}`;

      const t0 = performance.now();
      const res = await fetch(url, { method: op.method.toUpperCase(), headers, body });
      const ms = Math.round(performance.now() - t0);
      const text = await res.text();
      let pretty = text;
      try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch { /* keep raw */ }
      setTestResults((r) => ({ ...r, [idx]: { status: res.status, ms, body: pretty || '(empty)' } }));
    } catch (err) {
      setTestResults((r) => ({ ...r, [idx]: { status: 0, ms: 0, body: `Request failed: ${errorMessage(err)}` } }));
    } finally {
      setTestingIdx(null);
    }
  }, [ops, testUrls, testBodies, bearerToken, defaultUrl]);

  const curlFor = useCallback((op: WizardOp): string => {
    const url = `${targetBase}${DynamicApiService.joinPaths(basePath.trim() || '/', op.path)}`;
    let cmd = `curl -X ${op.method.toUpperCase()} '${url}'`;
    const token = bearerToken.trim();
    if (token) cmd += ` \\\n  -H 'Authorization: Bearer ${token}'`;
    return cmd;
  }, [targetBase, basePath, bearerToken]);

  if (!open) return null;

  const currentIdx = PHASES.findIndex((p) => p.id === phase);
  const METHOD_COLORS: Record<string, string> = { GET: 'text-emerald-400', POST: 'text-sky-400', PUT: 'text-violet-400', PATCH: 'text-amber-400', DELETE: 'text-red-400' };
  const opFullPaths = ops.map((o) => DynamicApiService.joinPaths(basePath.trim() || '/', o.path));

  // -- Phase 1: shell -----------------------------------------------------------
  const shellBody = (
    <div className="space-y-4">
      {!initialNodePath && (
        <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-[11px] text-amber-300 flex items-center gap-2">
          <AlertCircle size={13} /> Select a workspace node in the tree first — close this wizard, pick a node, then reopen it.
        </div>
      )}
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        <div>
          <label className={labelCls}>Name *</label>
          <input className={inputCls} value={name} onChange={(e) => setName(e.target.value)} placeholder="Order API" />
        </div>
        <div>
          <label className={labelCls}>Workspace node</label>
          <input className={`${inputCls} bg-gray-800/50 text-gray-400`} value={initialNodePath ?? '(none selected)'} readOnly />
        </div>
        <div>
          <label className={labelCls}>Base path</label>
          <input className={inputCls} value={basePath} onChange={(e) => setBasePath(e.target.value)} placeholder="/orders" />
        </div>
        <div>
          <label className={labelCls}>Attribute domain (optional)</label>
          <input className={inputCls} list="aiwiz-domains" value={attributeDomain} onChange={(e) => setAttributeDomain(e.target.value)} placeholder="existing domain or new name" />
          <datalist id="aiwiz-domains">
            {(ctx?.domains ?? []).map((d) => (<option key={d.name} value={d.name}>{d.attributes.length > 0 ? `${d.attributes.map((a) => a.name).join(', ')}` : ''}</option>))}
          </datalist>
        </div>
        <div className="col-span-2">
          <label className={labelCls}>Bearer token (optional — empty = open access)</label>
          <div className="flex gap-1.5">
            <input type="password" className={`${inputCls} font-mono`} value={bearerToken} onChange={(e) => setBearerToken(e.target.value)} placeholder="leave empty for open access" />
            <button className="btn btn-secondary !px-2 !py-1 text-xs shrink-0" onClick={() => setBearerToken(generateToken())} title="Generate a random 32-hex token">
              <KeyRound size={12} /> Generate
            </button>
          </div>
        </div>
        <div className="col-span-2">
          <label className={labelCls}>Description</label>
          <input className={inputCls} value={description} onChange={(e) => setDescription(e.target.value)} placeholder="What this API is for" />
        </div>
        <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="accent-indigo-500" /> Active (inactive APIs are skipped by the dispatcher)
        </label>
      </div>
      <div className="flex items-center justify-between pt-2 border-t border-gray-800">
        <button className="btn btn-secondary !px-3 !py-1.5 text-xs" onClick={onClose}>Cancel</button>
        <button className="btn btn-primary !px-3 !py-1.5 text-xs" disabled={!name.trim() || !initialNodePath} onClick={() => setPhase('operations')}>
          Continue to operations →
        </button>
      </div>
    </div>
  );

  // -- Phase 2: operations --------------------------------------------------------
  const operationsBody = (
    <div className="space-y-4">
      {/* Ops so far */}
      {ops.length > 0 ? (
        <div className="rounded-lg border border-gray-800 divide-y divide-gray-800">
          {ops.map((o, i) => (
            <div key={i} className="flex items-center gap-2 px-3 py-1.5 text-xs">
              <span className={`font-mono font-semibold w-14 ${METHOD_COLORS[o.method] ?? 'text-gray-300'}`}>{o.method}</span>
              <span className="font-mono text-gray-200">{opFullPaths[i]}</span>
              <span className="px-1.5 py-0.5 rounded bg-gray-800 text-[10px] text-gray-400">{o.handlerType}</span>
              {o.flowId && <span className="text-[10px] text-gray-500 truncate max-w-[160px]" title={o.flowId}>flow: {o.flowId}</span>}
              {(o.domainName || attributeDomain) && o.handlerType !== 'flow' && o.handlerType !== 'dataExchange' && (
                <span className="text-[10px] text-gray-500 truncate max-w-[160px]" title={o.domainName || attributeDomain}>domain: {o.domainName || attributeDomain}</span>
              )}
              {o.profileId && <span className="text-[10px] text-gray-500 truncate max-w-[160px]" title={o.profileId}>profile: {o.profileId}</span>}
              <button className="ml-auto text-gray-500 hover:text-red-400" onClick={() => setOps((prev) => prev.filter((_, j) => j !== i))} title="Remove operation">
                <Trash2 size={13} />
              </button>
            </div>
          ))}
        </div>
      ) : (
        <p className="text-[11px] text-gray-500">No operations yet — generate one below or fill in the draft form manually.</p>
      )}

      {/* AI generation */}
      <div className="rounded-lg border border-indigo-500/30 bg-indigo-500/5 p-3 space-y-2">
        <div className="flex items-center gap-2">
          <Sparkles size={14} className="text-indigo-400 shrink-0" />
          <span className="text-xs font-semibold text-gray-200">Generate with AI</span>
          {hasConfig && savedModel ? (
            <span className="text-[10px] text-gray-500 truncate">model: {savedModel}</span>
          ) : null}
          <button className="ml-auto flex items-center gap-1 text-[10px] text-gray-400 hover:text-gray-200" onClick={() => void loadContext()} disabled={ctxLoading}>
            <RefreshCw size={11} className={ctxLoading ? 'animate-spin' : ''} /> refresh context
          </button>
        </div>
        {ctx && (
          <p className="text-[10px] text-gray-500">
            Context: {ctx.domains.length} domain(s) · {Object.keys(ctx.sampleRows).length > 0 ? `${(ctx.sampleRows[Object.keys(ctx.sampleRows)[0]] ?? []).length} sample row(s)` : 'no sample rows'} · {ctx.flows.length} flow(s) · {ctx.profiles.length} profile(s)
          </p>
        )}
        {!hasConfig && (
          <div className="rounded border border-amber-500/40 bg-amber-500/10 px-2 py-1.5 text-[11px] text-amber-300 flex items-center gap-2">
            No AI model configured — open Settings → AI Model and save a local endpoint first.
            <button className="ml-auto underline hover:text-amber-200 shrink-0" onClick={openConfigModal}>Open settings</button>
          </div>
        )}
        {ctxError && <p className="text-[11px] text-red-400">Context error: {ctxError}</p>}
        <div className="flex gap-2">
          <select className={selectCls} value={genMethod} onChange={(e) => setGenMethod(e.target.value)}>
            {METHODS.map((m) => (<option key={m} value={m}>{m}</option>))}
          </select>
          <input className={`${inputCls} flex-1`} value={intent} onChange={(e) => setIntent(e.target.value)} placeholder="What should this operation do? e.g. list recent orders sorted by capture time" />
          <button className="btn btn-primary !px-3 !py-1 text-xs shrink-0 flex items-center gap-1.5" onClick={() => void handleGenerate()} disabled={generating || !hasConfig}>
            {generating ? <Loader2 size={12} className="animate-spin" /> : <Sparkles size={12} />} {generating ? 'Generating…' : 'Generate'}
          </button>
        </div>
        {genError && (
          <div className="rounded border border-red-500/40 bg-red-500/10 px-2 py-1.5 text-[11px] text-red-300 whitespace-pre-wrap flex items-start gap-2">
            <AlertCircle size={13} className="shrink-0 mt-0.5" /> {genError}
          </div>
        )}
      </div>

      {/* Editable draft form (always available — AI output is a starting point) */}
      <div className="rounded-lg border border-gray-800 p-3 space-y-2">
        <p className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold">Operation draft (edit anything)</p>
        <div className="grid grid-cols-3 gap-2">
          <div>
            <label className={labelCls}>Method</label>
            <select className={`${selectCls} w-full`} value={opForm.method} onChange={(e) => setOpForm((f) => ({ ...f, method: e.target.value }))}>
              {METHODS.map((m) => (<option key={m} value={m}>{m}</option>))}
            </select>
          </div>
          <div>
            <label className={labelCls}>Path (relative to base)</label>
            <input className={inputCls} value={opForm.path} onChange={(e) => setOpForm((f) => ({ ...f, path: e.target.value }))} placeholder='"" or "/{id}"' />
          </div>
          <div>
            <label className={labelCls}>Handler</label>
            <select className={`${selectCls} w-full`} value={opForm.handlerType} onChange={(e) => setOpForm((f) => ({ ...f, handlerType: e.target.value as HandlerType }))}>
              {HANDLER_TYPES.map((h) => (<option key={h} value={h}>{h}</option>))}
            </select>
          </div>
        </div>
        {opForm.handlerType === 'eav' && opForm.method === 'GET' && (
          <div className="flex items-center gap-1.5 text-[10px] text-gray-500">
            EAV GET shapes:
            {[['', '"" collection'], ['/{id}', '/{id} lookup'], ['/{id}/comments', '/{id}/… entity filter']].map(([p, label]) => (
              <button key={label} className={`px-1.5 py-0.5 rounded border ${opForm.path === p ? 'border-indigo-500 text-indigo-300' : 'border-gray-700 text-gray-400 hover:text-gray-200'}`} onClick={() => setOpForm((f) => ({ ...f, path: p }))}>
                {label}
              </button>
            ))}
          </div>
        )}
        <div className="grid grid-cols-3 gap-2">
          {opForm.handlerType === 'flow' && (
            <div>
              <label className={labelCls}>Flow id *</label>
              {(ctx?.flows.length ?? 0) > 0 ? (
                <select className={`${selectCls} w-full`} value={opForm.flowId} onChange={(e) => setOpForm((f) => ({ ...f, flowId: e.target.value }))}>
                  <option value="">— select flow —</option>
                  {ctx!.flows.map((fl) => (<option key={fl.id} value={fl.id}>{fl.name}</option>))}
                </select>
              ) : (
                <input className={inputCls} value={opForm.flowId} onChange={(e) => setOpForm((f) => ({ ...f, flowId: e.target.value }))} placeholder="flow id" />
              )}
            </div>
          )}
          {(opForm.handlerType === 'attributeDomain' || opForm.handlerType === 'eav') && (
            <div>
              <label className={labelCls}>Domain name {attributeDomain ? `(blank = api-level "${attributeDomain}")` : '*'}</label>
              <input className={inputCls} list="aiwiz-domains" value={opForm.domainName} onChange={(e) => setOpForm((f) => ({ ...f, domainName: e.target.value }))} placeholder={attributeDomain || 'domain name'} />
            </div>
          )}
          {opForm.handlerType === 'dataExchange' && (
            <div>
              <label className={labelCls}>Profile *</label>
              {(ctx?.profiles.length ?? 0) > 0 ? (
                <select className={`${selectCls} w-full`} value={opForm.profileId} onChange={(e) => setOpForm((f) => ({ ...f, profileId: e.target.value }))}>
                  <option value="">— select profile —</option>
                  {ctx!.profiles.map((p) => (<option key={p.id} value={p.id}>{p.name}</option>))}
                </select>
              ) : (
                <input className={inputCls} value={opForm.profileId} onChange={(e) => setOpForm((f) => ({ ...f, profileId: e.target.value }))} placeholder="profile id or name" />
              )}
            </div>
          )}
          <div className={opForm.handlerType === 'flow' || opForm.handlerType === 'dataExchange' ? '' : 'col-span-2'}>
            <label className={labelCls}>Description</label>
            <input className={inputCls} value={opForm.description} onChange={(e) => setOpForm((f) => ({ ...f, description: e.target.value }))} placeholder="what this operation does" />
          </div>
        </div>
        {formErrors.length > 0 && (
          <ul className="text-[11px] text-red-400 space-y-0.5">
            {formErrors.map((e, i) => (<li key={i}>• {e}</li>))}
          </ul>
        )}
        <div className="flex justify-end">
          <button className="btn btn-secondary !px-3 !py-1 text-xs flex items-center gap-1.5" onClick={addOpToOps}>
            <Plus size={12} /> Add to operations
          </button>
        </div>
      </div>

      <div className="flex items-center justify-between pt-2 border-t border-gray-800">
        <button className="btn btn-secondary !px-3 !py-1.5 text-xs flex items-center gap-1" onClick={() => setPhase('shell')}><ChevronLeft size={12} /> Back</button>
        <button className="btn btn-primary !px-3 !py-1.5 text-xs" disabled={ops.length === 0} onClick={() => { setSaveError(''); setPhase('review'); }}>Review & save →</button>
      </div>
    </div>
  );

  // -- Phase 3: review & save ------------------------------------------------------
  const reviewBody = (
    <div className="space-y-4">
      <div className="rounded-lg border border-gray-800 overflow-hidden">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-gray-800/60 text-left text-[10px] uppercase tracking-wider text-gray-500">
              <th className="px-3 py-1.5">#</th>
              <th className="px-2 py-1.5">Method</th>
              <th className="px-2 py-1.5">Full path</th>
              <th className="px-2 py-1.5">Handler</th>
              <th className="px-2 py-1.5">Target</th>
              <th className="px-2 py-1.5">Description</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-800">
            {ops.map((o, i) => (
              <tr key={i}>
                <td className="px-3 py-1.5 text-gray-500">{i + 1}</td>
                <td className={`px-2 py-1.5 font-mono font-semibold ${METHOD_COLORS[o.method] ?? 'text-gray-300'}`}>{o.method}</td>
                <td className="px-2 py-1.5 font-mono text-gray-200">{opFullPaths[i]}</td>
                <td className="px-2 py-1.5"><span className="px-1.5 py-0.5 rounded bg-gray-800 text-[10px] text-gray-400">{o.handlerType}</span></td>
                <td className="px-2 py-1.5 text-gray-400 max-w-[160px] truncate" title={o.flowId || o.domainName || o.profileId || ''}>{o.handlerType === 'flow' ? (o.flowId || '—') : o.handlerType === 'dataExchange' ? (o.profileId || '—') : (o.domainName || attributeDomain || '—')}</td>
                <td className="px-2 py-1.5 text-gray-400 max-w-[220px] truncate" title={o.description}>{o.description || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div>
        <p className={`${labelCls} mb-1`}>Payload (POST /api/dynamic/apis)</p>
        <pre className="rounded-lg border border-gray-800 bg-gray-950 p-3 text-[10px] leading-relaxed text-gray-300 overflow-auto max-h-48 whitespace-pre">{JSON.stringify(buildPayload(), null, 2)}</pre>
      </div>

      <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
        <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} className="accent-indigo-500" /> Publish to Dynamic API hosts (isPublished — external hosts pick it up within ~30 s)
      </label>

      {saveError && (
        <div className="rounded-lg border border-red-500/40 bg-red-500/10 px-3 py-2 text-[11px] text-red-300 whitespace-pre-wrap">
          <p className="font-semibold flex items-center gap-1.5 mb-1"><AlertCircle size={13} /> Save failed — server response:</p>
          {saveError}
        </div>
      )}

      <div className="flex items-center justify-between pt-2 border-t border-gray-800">
        <button className="btn btn-secondary !px-3 !py-1.5 text-xs flex items-center gap-1" onClick={() => setPhase('operations')}><ChevronLeft size={12} /> Back to operations</button>
        <div className="flex items-center gap-2">
          {saveError && (
            <button className="btn btn-secondary !px-3 !py-1.5 text-xs" onClick={() => setPhase('operations')}>Regenerate conflicting op</button>
          )}
          <button className="btn btn-primary !px-3 !py-1.5 text-xs flex items-center gap-1.5" onClick={() => void saveApi()} disabled={saving}>
            {saving ? <Loader2 size={12} className="animate-spin" /> : <Save size={12} />} Save API
          </button>
        </div>
      </div>
    </div>
  );

  // -- Phase 4: test & deploy --------------------------------------------------------
  const testBody = (
    <div className="space-y-4">
      {savedId && (
        <p className="text-[11px] text-emerald-400 flex items-center gap-1.5"><CheckCircle2 size={13} /> Saved as {savedId}{published ? ' · published' : ''}</p>
      )}

      {/* Target */}
      <div className="rounded-lg border border-gray-800 p-3 space-y-2">
        <p className={labelCls}>Test target</p>
        <div className="flex items-center gap-4 text-xs text-gray-300">
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="radio" name="aiwiz-target" checked={targetMode === 'engine'} onChange={() => setTargetMode('engine')} className="accent-indigo-500" /> Engine (same origin)
          </label>
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="radio" name="aiwiz-target" checked={targetMode === 'host'} onChange={() => setTargetMode('host')} className="accent-indigo-500" /> DynamicApiHost
          </label>
        </div>
        {targetMode === 'host' && (
          <div className="flex items-center gap-2">
            <input className={`${inputCls} flex-1 font-mono`} value={hostUrl} onChange={(e) => setHostUrl(e.target.value)} placeholder="http://localhost:5002/api/dynamic" />
            <button className="btn btn-secondary !px-2.5 !py-1 text-xs shrink-0 flex items-center gap-1" onClick={() => void runHealthCheck()} disabled={health.state === 'checking' || !hostBase}>
              <RefreshCw size={11} className={health.state === 'checking' ? 'animate-spin' : ''} /> Check health
            </button>
          </div>
        )}
        {targetMode === 'host' && (
          <p className="text-[10px] text-gray-500">
            DynamicApiHost polls the engine every 30 s — a newly saved/published API appears within about a minute.
            {health.state === 'ok' && <span className="text-emerald-400 ml-2 flex items-center gap-1"><CheckCircle2 size={11} /> {health.detail}</span>}
            {health.state === 'error' && <span className="text-red-400 ml-2">unreachable: {health.detail}</span>}
          </p>
        )}
      </div>

      {/* Per-op test rows */}
      <div className="space-y-3">
        {ops.map((op, i) => (
          <div key={i} className="rounded-lg border border-gray-800 p-3 space-y-2">
            <div className="flex items-center gap-2 text-xs">
              <span className={`font-mono font-semibold ${METHOD_COLORS[op.method] ?? 'text-gray-300'}`}>{op.method}</span>
              <span className="font-mono text-gray-400">{DynamicApiService.joinPaths(basePath.trim() || '/', op.path)}</span>
              <button className="ml-auto flex items-center gap-1 text-[10px] text-gray-500 hover:text-gray-300" onClick={() => { void copyText(curlFor(op)); showToast({ type: 'success', message: 'curl command copied.' }); }} title="Copy curl command">
                <Copy size={11} /> curl
              </button>
            </div>
            <input className={`${inputCls} font-mono`} value={testUrls[i] ?? defaultUrl(i)} onChange={(e) => setTestUrls((m) => ({ ...m, [i]: e.target.value }))} placeholder="request URL" />
            {op.method !== 'GET' && op.method !== 'DELETE' && (
              <textarea className={`${inputCls} font-mono h-16 resize-y`} value={testBodies[i] ?? ''} onChange={(e) => setTestBodies((m) => ({ ...m, [i]: e.target.value }))} placeholder='JSON body, e.g. {"orderNumber":"A-1","total":9}' />
            )}
            <div className="flex items-center gap-2">
              <button className="btn btn-primary !px-3 !py-1 text-xs flex items-center gap-1.5" onClick={() => void sendTest(i)} disabled={testingIdx === i}>
                {testingIdx === i ? <Loader2 size={12} className="animate-spin" /> : <Play size={12} />} Send
              </button>
              {testResults[i] && (
                <span className={`text-[11px] font-mono ${testResults[i]!.status >= 200 && testResults[i]!.status < 300 ? 'text-emerald-400' : 'text-red-400'}`}>
                  {testResults[i]!.status === 0 ? 'error' : `${testResults[i]!.status}`} · {testResults[i]!.ms} ms
                </span>
              )}
            </div>
            {testResults[i] && (
              <pre className="rounded border border-gray-800 bg-gray-950 p-2 text-[10px] leading-relaxed text-gray-300 overflow-auto max-h-40 whitespace-pre-wrap break-all">{testResults[i]!.body}</pre>
            )}
          </div>
        ))}
      </div>

      {/* Deploy panel */}
      <div className="rounded-lg border border-gray-800 p-3 space-y-2">
        <p className={labelCls}>Deploy</p>
        <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
          <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} className="accent-indigo-500" /> Published to Dynamic API hosts
        </label>
        <div className="flex items-center gap-2">
          <button className="btn btn-primary !px-3 !py-1 text-xs flex items-center gap-1.5" onClick={() => void saveApi()} disabled={saving}>
            {saving ? <Loader2 size={12} className="animate-spin" /> : <Rocket size={12} />} Save & publish state
          </button>
          {saveError && <span className="text-[11px] text-red-400 truncate max-w-[320px]" title={saveError}>{saveError}</span>}
        </div>
      </div>

      <div className="flex items-center justify-between pt-2 border-t border-gray-800">
        <button className="btn btn-secondary !px-3 !py-1.5 text-xs flex items-center gap-1" onClick={() => setPhase('review')}><ChevronLeft size={12} /> Back to review</button>
        <button className="btn btn-secondary !px-3 !py-1.5 text-xs" onClick={onClose}>Done</button>
      </div>
    </div>
  );

  // -- Modal frame ---------------------------------------------------------------------
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="w-[880px] max-w-[94vw] h-[86vh] max-h-[760px] flex flex-col rounded-xl border border-gray-700 bg-gray-900 shadow-2xl">
        {/* Header */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-800 shrink-0">
          <Sparkles size={16} className="text-indigo-400" />
          <h2 className="text-sm font-semibold text-gray-100">AI Dynamic API Wizard</h2>
          <div className="flex items-center gap-1 ml-3">
            {PHASES.map((p, i) => (
              <button
                key={p.id}
                disabled={i > currentIdx}
                onClick={() => setPhase(p.id)}
                className={`px-2 py-0.5 rounded-full text-[10px] font-medium ${i === currentIdx ? 'bg-indigo-600 text-white' : i < currentIdx ? 'bg-gray-800 text-gray-300 hover:bg-gray-700 cursor-pointer' : 'bg-gray-800/50 text-gray-600 cursor-default'}`}
              >
                {i + 1}. {p.label}
              </button>
            ))}
          </div>
          <button className="ml-auto text-gray-400 hover:text-gray-200" onClick={onClose} title="Close">
            <X size={16} />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-4 py-3">
          {phase === 'shell' && shellBody}
          {phase === 'operations' && operationsBody}
          {phase === 'review' && reviewBody}
          {phase === 'test' && testBody}
        </div>
      </div>
    </div>
  );
}
