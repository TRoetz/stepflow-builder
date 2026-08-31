import { useState, useCallback, useEffect } from 'react';
import {
  Globe, X, Maximize2, Minimize2, Plus, Trash2, Save, Lock, Copy, Play, FileJson, AlertCircle, RefreshCw, KeyRound, ChevronRight, ChevronDown, Sparkles,
} from 'lucide-react';
import {
  DynamicApiService, type DynamicApiDefinition, type DynamicApiOperation, type HandlerType,
} from '@services/dynamicApiService';
import { useWorkspaceStore } from '@stores/useWorkspaceStore';
import { showToast } from '@stores/useToastStore';
import { AiDynamicApiWizard } from './AiDynamicApiWizard';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const;
const HANDLER_TYPES: HandlerType[] = ['flow', 'attributeDomain', 'eav', 'dataExchange'];

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
  } catch { /* clipboard unavailable - ignore */ }
}

/** Option shapes from the lookup endpoints (only the fields this panel needs). */
interface DomainEntry { attributeDomain?: { attributeDomainName?: string } | null; }
interface FlowOption { id: string | number; name?: string; }
interface ProfileOption { dataExchangeProfileId?: number; dataExchangeProfileName?: string; }

/** Editor draft - all optionals flattened to plain strings so inputs stay controlled. */
interface DraftOp {
  method: string;
  path: string;
  handlerType: HandlerType;
  flowId: string;
  domainName: string;
  profileId: string;
  description: string;
}

interface DraftApi {
  id?: string;
  name: string;
  description: string;
  nodePath: string;
  basePath: string;
  attributeDomain: string;
  bearerToken: string;
  isActive: boolean;
  published: boolean;
  operations: DraftOp[];
}

function toDraft(def: DynamicApiDefinition): DraftApi {
  return {
    id: def.id,
    name: def.name ?? '',
    description: def.description ?? '',
    nodePath: def.nodePath ?? '',
    basePath: def.basePath || '/',
    attributeDomain: def.attributeDomain ?? '',
    bearerToken: def.bearerToken ?? '',
    isActive: def.isActive !== false,
    published: def.isPublished === true,
    operations: (def.operations ?? []).map((o) => ({
      method: o.method || 'GET',
      path: o.path ?? '',
      handlerType: (o.handlerType as HandlerType) || 'flow',
      flowId: o.flowId ?? '',
      domainName: o.domainName ?? def.attributeDomain ?? '',
      profileId: o.profileId ?? '',
      description: o.description ?? '',
    })),
  };
}

/** Default values for a freshly added operation (spread at call sites). */
const NEW_OP: DraftOp = { method: 'GET', path: '', handlerType: 'eav', flowId: '', domainName: '', profileId: '', description: '' };

const inputCls = 'w-full bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-indigo-500';
const selectCls = 'bg-gray-800 border border-gray-700 rounded px-1.5 py-1 text-xs text-gray-200 focus:outline-none focus:border-indigo-500';
const labelCls = 'text-[10px] uppercase tracking-wider text-gray-500 font-semibold';

interface DynamicApiPanelProps {
  onClose: () => void;
}

export function DynamicApiPanel({ onClose }: DynamicApiPanelProps) {
  const tree = useWorkspaceStore((s) => s.tree);
  const loadTree = useWorkspaceStore((s) => s.loadTree);
  const storeError = useWorkspaceStore((s) => s.error);

  const [isMaximized, setIsMaximized] = useState(false);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [selectedNodePath, setSelectedNodePath] = useState<string | null>(null);
  const [apis, setApis] = useState<DynamicApiDefinition[]>([]);
 const [wizardOpen, setWizardOpen] = useState(false);

  // Option sources for the editor selects.
  const [domainNames, setDomainNames] = useState<string[]>([]);
  const [flows, setFlows] = useState<{ id: string; name: string }[]>([]);
  const [profiles, setProfiles] = useState<{ id: string; name: string }[]>([]);

  // Editor draft (right column).
  const [draft, setDraft] = useState<DraftApi | null>(null);
  const [saving, setSaving] = useState(false);

  // Bottom tabs.
  type TabId = 'openapi' | 'test';
  const [tab, setTab] = useState<TabId>('openapi');
  const [specText, setSpecText] = useState<string | null>(null);
  const [loadingSpec, setLoadingSpec] = useState(false);

  // Test tab state.
  const [selectedOpIndex, setSelectedOpIndex] = useState(0);
  const [testMethod, setTestMethod] = useState('GET');
  const [testUrl, setTestUrl] = useState('/api/dynamic/');
  const [authHeader, setAuthHeader] = useState('');
  const [sending, setSending] = useState(false);
  const [testStatus, setTestStatus] = useState<string | null>(null);
  const [testBody, setTestBody] = useState('');
  const [testBodyInput, setTestBodyInput] = useState('');

  // Workspace tree + option sources on mount.
  useEffect(() => {
    loadTree();
    (async () => {
      try {
        const res = await fetch('/api/attribute-domains');
        if (res.ok) {
          const entries = (await res.json()) as DomainEntry[];
          setDomainNames(entries.map((e) => e.attributeDomain?.attributeDomainName ?? '').filter(Boolean));
        }
      } catch { /* keep empty */ }
      try {
        const res = await fetch('/api/flows');
        if (res.ok) {
          const list = (await res.json()) as FlowOption[];
          setFlows(list.map((f) => ({ id: String(f.id), name: f.name || String(f.id) })));
        }
      } catch { /* keep empty */ }
      try {
        const res = await fetch('/api/data-exchange/profiles');
        if (res.ok) {
          const list = (await res.json()) as ProfileOption[];
          setProfiles(list.map((p) => ({ id: p.dataExchangeProfileName ?? String(p.dataExchangeProfileId), name: p.dataExchangeProfileName ?? `#${p.dataExchangeProfileId}` })));
        }
      } catch { /* keep empty */ }
    })();
  }, [loadTree]);

  const refreshApis = useCallback(async (): Promise<DynamicApiDefinition[]> => {
    try {
      const list = await DynamicApiService.list(selectedNodePath ?? undefined);
      setApis(list);
      return list;
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
      return [];
    }
  }, [selectedNodePath]);

  useEffect(() => { void refreshApis(); }, [refreshApis]);

  const toggleExpand = (path: string) => setExpanded((prev) => ({ ...prev, [path]: !prev[path] }));

  /** Prefills the Test tab from a freshly loaded draft: first GET op (or first op), bearer token. */
  const prefillTest = useCallback((d: DraftApi) => {
    const firstGet = d.operations.findIndex((o) => o.method === 'GET');
    const idx = firstGet >= 0 ? firstGet : Math.max(0, d.operations.length - 1);
    setSelectedOpIndex(d.operations.length > 0 ? idx : 0);
    setAuthHeader(d.bearerToken ? `Bearer ${d.bearerToken}` : '');
    if (d.operations.length > 0) {
      const op = d.operations[idx];
      setTestMethod(op.method);
      setTestUrl(DynamicApiService.requestUrl(d.basePath || '/', op.path));
    } else {
      setTestMethod('GET');
      setTestUrl('/api/dynamic/');
    }
  }, []);

  const handleLoadApi = useCallback((def: DynamicApiDefinition) => {
    const d = toDraft(def);
    setDraft(d);
    prefillTest(d);
  }, [prefillTest]);

  /** Wizard finished saving — refresh the list and load the new API into the editor. */
  const handleWizardSaved = useCallback(async (id: string) => {
    const list = await refreshApis();
    const def = list.find((a) => a.id === id);
    if (def) handleLoadApi(def);
  }, [refreshApis, handleLoadApi]);

  const handleNewApi = () => {
    if (!selectedNodePath) {
      showToast({ type: 'error', message: 'Select a workspace node first' });
      return;
    }
    const d: DraftApi = {
      name: '', description: '', nodePath: selectedNodePath, basePath: '/',
      attributeDomain: domainNames[0] ?? '', bearerToken: '', isActive: true, published: false,
      operations: [{ ...NEW_OP, domainName: domainNames[0] ?? '' }],
    };
    setDraft(d);
    prefillTest(d);
  };

  const handleDelete = async (def: DynamicApiDefinition) => {
    if (!window.confirm(`Delete API "${def.name}"?`)) return;
    try {
      await DynamicApiService.remove(def.id);
      if (draft?.id === def.id) setDraft(null);
      void refreshApis();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    }
  };

  const updateOp = (index: number, patch: Partial<DraftOp>) => {
    setDraft((d) => d ? { ...d, operations: d.operations.map((o, i) => (i === index ? { ...o, ...patch } : o)) } : d);
  };

  const handleSave = async () => {
    if (!draft || saving) return;
    const name = draft.name.trim();
    if (!name) { showToast({ type: 'error', message: 'Name is required' }); return; }
    if (!draft.nodePath) { showToast({ type: 'error', message: 'Workspace node path is missing - select a node in the tree' }); return; }

    let basePath = (draft.basePath || '/').trim() || '/';
    if (!basePath.startsWith('/')) basePath = '/' + basePath;

    const operations: DynamicApiOperation[] = [];
    for (const o of draft.operations) {
      const op: DynamicApiOperation = { method: o.method, path: o.path.trim(), handlerType: o.handlerType };
      if (o.description.trim()) op.description = o.description.trim();
      switch (o.handlerType) {
        case 'flow':
          if (!o.flowId) { showToast({ type: 'error', message: `Flow id is required for ${o.method} ${o.path || basePath}` }); return; }
          op.flowId = o.flowId;
          break;
        case 'dataExchange':
          if (!o.profileId) { showToast({ type: 'error', message: `Profile id is required for ${o.method} ${o.path || basePath}` }); return; }
          op.profileId = o.profileId;
          break;
        case 'attributeDomain':
        case 'eav': {
          const domain = o.domainName || draft.attributeDomain;
          if (!domain) { showToast({ type: 'error', message: `Attribute domain is required for ${o.method} ${o.path || basePath}` }); return; }
          op.domainName = domain;
          break;
        }
      }
      operations.push(op);
    }

    setSaving(true);
    try {
      const res = await DynamicApiService.save({
        id: draft.id, name, description: draft.description.trim() || null, nodePath: draft.nodePath, basePath,
        attributeDomain: draft.attributeDomain || null, bearerToken: draft.bearerToken || null, isActive: draft.isActive, isPublished: draft.published, operations,
      });
      showToast({ type: 'success', message: res.created ? `API "${name}" created` : `API "${name}" saved` });
      setDraft((d) => (d ? { ...d, id: res.id } : d));
      void refreshApis();
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setSaving(false);
    }
  };

  const handleLoadSpec = async () => {
    setLoadingSpec(true);
    try {
      const spec = await DynamicApiService.openApi();
      setSpecText(JSON.stringify(spec, null, 2));
    } catch (err) {
      showToast({ type: 'error', message: errorMessage(err) });
    } finally {
      setLoadingSpec(false);
    }
  };

  const handleSend = async () => {
    if (!testUrl || sending) return;
    setSending(true);
    setTestStatus(null);
    setTestBody('');
    try {
      const headers: Record<string, string> = {};
      if (authHeader.trim()) headers['Authorization'] = authHeader.trim();
      const init: RequestInit = { method: testMethod, headers };
      const bodyText = testBodyInput.trim();
      if (bodyText && !/^(GET|HEAD)$/i.test(testMethod)) {
        headers['Content-Type'] = 'application/json';
        init.body = bodyText;
      }
      const res = await fetch(testUrl, init);
      const text = await res.text();
      setTestStatus(`${res.status} ${res.statusText}`);
      try {
        setTestBody(JSON.stringify(JSON.parse(text), null, 2));
      } catch {
        setTestBody(text || '(empty body)');
      }
    } catch (err) {
      setTestStatus('network error');
      setTestBody(errorMessage(err));
    } finally {
      setSending(false);
    }
  };

  const handleOpSelect = (index: number) => {
    setSelectedOpIndex(index);
    if (!draft || index < 0 || !draft.operations[index]) return;
    const op = draft.operations[index];
    setTestMethod(op.method);
    setTestUrl(DynamicApiService.requestUrl(draft.basePath || '/', op.path));
  };

  // ── Left column: node tree row (any depth 1-3 selectable) ────────────────────────
  const renderNodeRow = (level: number, name: string, path: string, hasChildren: boolean) => {
    const indentClass = level === 0 ? 'pl-2' : level === 1 ? 'pl-7' : 'pl-12';
    const selected = selectedNodePath?.toLowerCase() === path.toLowerCase();
    return (
      <div
        key={path}
        className={`group flex items-center gap-1 py-1 rounded cursor-pointer ${indentClass} ${selected ? 'bg-indigo-500/10 ring-1 ring-indigo-500/30' : 'hover:bg-gray-800/50'}`}
        onClick={() => setSelectedNodePath(path)}
      >
        {hasChildren ? (
          <button className="text-gray-500 hover:text-gray-300 shrink-0" title={expanded[path] ? 'Collapse' : 'Expand'}
            onClick={(e) => { e.stopPropagation(); toggleExpand(path); }}>
            {expanded[path] ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
          </button>
        ) : (
          <span className="w-4 shrink-0" />
        )}
        <span className={`text-xs truncate ${selected ? 'text-gray-100' : 'text-gray-300'}`}>{name}</span>
      </div>
    );
  };

  return (
    <div className={isMaximized ? 'fixed inset-0 z-50 flex flex-col bg-gray-900' : 'w-[880px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800'}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <Globe className="w-4 h-4 text-indigo-400" />
          Dynamic API
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title="AI Wizard — guided dynamic API creation" onClick={() => setWizardOpen(true)}>
            <Sparkles className="w-4 h-4 text-indigo-400" />
          </button>
          <button className="btn-icon" title={isMaximized ? 'Restore' : 'Expand'} onClick={() => setIsMaximized(!isMaximized)}>
            {isMaximized ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
          </button>
          <button className="btn-icon" title="Close Dynamic API Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body: two columns */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left column: node tree + API list for the selected node */}
        <div className="w-[260px] shrink-0 border-r border-gray-800 flex flex-col min-h-0">
          {storeError && (
            <div className="flex items-center gap-2 px-4 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {storeError}
            </div>
          )}

          {/* Tree */}
          <div className="p-2 border-b border-gray-800">
            <div className="flex items-center justify-between px-2 py-1">
              <span className={labelCls}>Workspace Node</span>
            </div>
            {!tree || tree.orgs.length === 0 ? (
              <div className="px-3 py-2 text-xs text-gray-500">No workspace nodes yet.</div>
            ) : (
              tree.orgs.map((org) => (
                <div key={org.name}>
                  {renderNodeRow(0, org.name, org.name, org.projects.length > 0)}
                  {expanded[org.name] && org.projects.map((project) => (
                    <div key={`${org.name}/${project.name}`}>
                      {renderNodeRow(1, project.name, `${org.name}/${project.name}`, project.subProjects.length > 0)}
                      {expanded[`${org.name}/${project.name}`] && project.subProjects.map((sub) =>
                        renderNodeRow(2, sub.name, `${org.name}/${project.name}/${sub.name}`, false))}
                    </div>
                  ))}
                </div>
              ))
            )}
          </div>

          {/* API list for the selected node */}
          <div className="flex-1 overflow-y-auto p-2 min-h-0">
            <div className="flex items-center justify-between px-2 py-1">
              <span className={labelCls}>APIs {selectedNodePath ? `· ${selectedNodePath}` : ''}</span>
              <button className="btn-icon !p-1" title="New API" onClick={handleNewApi}>
                <Plus className="w-3.5 h-3.5" />
              </button>
            </div>
            {!selectedNodePath ? (
              <div className="px-3 py-2 text-xs text-gray-500">Select a node above to list its APIs.</div>
            ) : apis.length === 0 ? (
              <div className="px-3 py-2 text-xs text-gray-500">No APIs on this node yet. Click + to create one.</div>
            ) : (
              apis.map((api) => {
                const selected = draft?.id !== undefined && api.id === draft.id;
                return (
                  <div key={api.id}
                    className={`group flex items-center gap-1 px-2 py-1 rounded cursor-pointer ${selected ? 'bg-indigo-500/10 ring-1 ring-indigo-500/30' : 'hover:bg-gray-800/50'}`}
                    onClick={() => handleLoadApi(api)}>
                    <span className="text-xs text-gray-200 truncate flex-1">{api.name}</span>
                    {api.bearerToken && <span title="Bearer token required" className="shrink-0"><Lock className="w-3 h-3 text-amber-400" /></span>}
                    {!api.isActive && <span className="text-[9px] uppercase text-gray-600 shrink-0">off</span>}
                    {api.isPublished && <span title="Published - exposed on external dynamic API hosts" className="text-[9px] uppercase text-emerald-400 shrink-0">pub</span>}
                    <button className="opacity-0 group-hover:opacity-100 text-gray-500 hover:text-red-400 shrink-0" title="Delete API"
                      onClick={(e) => { e.stopPropagation(); void handleDelete(api); }}>
                      <Trash2 className="w-3 h-3" />
                    </button>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {/* Right column: editor form + OpenAPI/Test tabs */}
        <div className="flex-1 min-w-0 flex flex-col min-h-0">
          {draft ? (
            <div className="flex-1 overflow-y-auto p-4 space-y-3 min-h-0">
              {/* Definition fields */}
              <div className="grid grid-cols-2 gap-x-3 gap-y-2">
                <div>
                  <label className={labelCls}>Name *</label>
                  <input className={inputCls} value={draft.name} placeholder="Order API"
                    onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
                </div>
                <div>
                  <label className={labelCls}>Description</label>
                  <input className={inputCls} value={draft.description} placeholder="What this API exposes"
                    onChange={(e) => setDraft({ ...draft, description: e.target.value })} />
                </div>
                <div>
                  <label className={labelCls}>Base Path</label>
                  <input className={inputCls} value={draft.basePath} placeholder="/orders"
                    onChange={(e) => setDraft({ ...draft, basePath: e.target.value })} />
                </div>
                <div>
                  <label className={labelCls}>Attribute Domain</label>
                  <select className={`${selectCls} w-full`} value={draft.attributeDomain}
                    onChange={(e) => {
                      const domain = e.target.value;
                      // Keep ops that fall back to the api-level domain in sync.
                      setDraft({ ...draft, attributeDomain: domain, operations: draft.operations.map((o) => o.domainName ? o : { ...o, domainName: domain }) });
                    }}>
                    <option value="">(none)</option>
                    {domainNames.map((d) => <option key={d} value={d}>{d}</option>)}
                  </select>
                </div>
                <div className="col-span-2">
                  <label className={labelCls}>Bearer Token (empty = open access)</label>
                  <div className="flex items-center gap-1.5">
                    <input type="password" className={`${inputCls} flex-1 font-mono`} value={draft.bearerToken} placeholder="no token - anyone can call"
                      onChange={(e) => {
                        const token = e.target.value;
                        setDraft({ ...draft, bearerToken: token });
                        setAuthHeader(token ? `Bearer ${token}` : '');
                      }} />
                    <button className="btn btn-secondary !px-2 !py-1 text-xs shrink-0" title="Generate a random 32-hex token"
                      onClick={() => { const t = generateToken(); setDraft({ ...draft, bearerToken: t }); setAuthHeader(`Bearer ${t}`); }}>
                      <KeyRound className="w-3 h-3" /> Generate
                    </button>
                    <button className="btn-icon shrink-0" title="Copy token to clipboard" onClick={() => void copyText(draft.bearerToken)}>
                      <Copy className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>
              </div>

              <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
                <input type="checkbox" className="accent-indigo-500" checked={draft.isActive}
                  onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} />
                Active (inactive APIs are skipped by the dispatcher and OpenAPI spec)
              </label>
              <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
                <input type="checkbox" className="accent-indigo-500" checked={draft.published}
                  onChange={(e) => setDraft({ ...draft, published: e.target.checked })} />
                Published (exposed on external dynamic API hosts)
              </label>

              {/* Operations */}
              <div>
                <div className="flex items-center justify-between mb-1">
                  <span className={labelCls}>Operations</span>
                  <button className="btn btn-secondary !px-2 !py-0.5 text-xs" onClick={() => setDraft({ ...draft, operations: [...draft.operations, { ...NEW_OP, domainName: draft.attributeDomain }] })}>
                    <Plus className="w-3 h-3" /> Add operation
                  </button>
                </div>
                {draft.operations.length === 0 && (
                  <div className="text-xs text-gray-500 px-1 py-2">No operations - add at least one.</div>
                )}
                <div className="space-y-2">
                  {draft.operations.map((op, i) => (
                    <div key={i} className="border border-gray-800 rounded-lg p-2 space-y-1.5 bg-gray-900/60">
                      <div className="flex items-center gap-1.5">
                        <select className={`${selectCls} w-[74px] shrink-0`} value={op.method} onChange={(e) => updateOp(i, { method: e.target.value })}>
                          {METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
                        </select>
                        <input className={`${inputCls} flex-1 min-w-0 font-mono`} value={op.path} placeholder='"/{id}" or "" for the base path'
                          onChange={(e) => updateOp(i, { path: e.target.value })} />
                        <select className={`${selectCls} w-[128px] shrink-0`} value={op.handlerType}
                          onChange={(e) => updateOp(i, { handlerType: e.target.value as HandlerType })}>
                          {HANDLER_TYPES.map((h) => <option key={h} value={h}>{h}</option>)}
                        </select>
                        {/* Handler-specific target */}
                        {op.handlerType === 'flow' && (
                          <select className={`${selectCls} flex-1 min-w-0`} value={op.flowId} onChange={(e) => updateOp(i, { flowId: e.target.value })}>
                            <option value="">(pick a flow)</option>
                            {flows.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
                          </select>
                        )}
                        {op.handlerType === 'dataExchange' && (
                          <select className={`${selectCls} flex-1 min-w-0`} value={op.profileId} onChange={(e) => updateOp(i, { profileId: e.target.value })}>
                            <option value="">(pick a profile)</option>
                            {profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                          </select>
                        )}
                        {(op.handlerType === 'attributeDomain' || op.handlerType === 'eav') && (
                          <select className={`${selectCls} flex-1 min-w-0`} value={op.domainName} onChange={(e) => updateOp(i, { domainName: e.target.value })}>
                            <option value="">(api-level domain)</option>
                            {domainNames.map((d) => <option key={d} value={d}>{d}</option>)}
                          </select>
                        )}
                        <button className="text-gray-500 hover:text-red-400 shrink-0" title="Remove operation" onClick={() => setDraft({ ...draft, operations: draft.operations.filter((_, j) => j !== i) })}>
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                      <input className={`${inputCls} !py-0.5 text-[11px]`} value={op.description} placeholder="Operation description (shown in the OpenAPI spec)"
                        onChange={(e) => updateOp(i, { description: e.target.value })} />
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex items-center justify-end gap-2 pt-1">
                <button className="btn btn-primary !py-1 text-xs" disabled={saving} onClick={() => void handleSave()}>
                  <Save className="w-3.5 h-3.5" /> {saving ? 'Saving…' : draft.id ? 'Save changes' : 'Create API'}
                </button>
              </div>
            </div>
          ) : (
            <div className="flex-1 flex items-center justify-center text-xs text-gray-500">
              Select an API on the left, or click + to create a new one.
            </div>
          )}

          {/* Bottom tabs: OpenAPI / Test */}
          <div className="border-t border-gray-800 flex flex-col h-[240px] shrink-0">
            <div className="flex items-center gap-1 px-3 pt-2">
              {(['openapi', 'test'] as TabId[]).map((t) => (
                <button key={t}
                  className={`text-xs px-2 py-1 rounded ${tab === t ? 'bg-indigo-500/20 text-indigo-300' : 'text-gray-400 hover:text-gray-200'}`}
                  onClick={() => setTab(t)}>
                  {t === 'openapi' ? 'OpenAPI' : 'Test'}
                </button>
              ))}
            </div>

            <div className="flex-1 overflow-auto p-3 min-h-0">
              {tab === 'openapi' && (
                <div className="space-y-2 h-full flex flex-col">
                  <div className="flex items-center gap-1.5 shrink-0">
                    <button className="btn btn-secondary !px-2 !py-0.5 text-xs" disabled={loadingSpec} onClick={() => void handleLoadSpec()}>
                      {loadingSpec ? <RefreshCw className="w-3 h-3 animate-spin" /> : <FileJson className="w-3 h-3" />} Load spec
                    </button>
                    <button className="btn btn-secondary !px-2 !py-0.5 text-xs" title="Copy the spec URL to the clipboard" onClick={() => void copyText('/api/dynamic/openapi.json')}>
                      <Copy className="w-3 h-3" /> Copy URL
                    </button>
                    <span className="text-[10px] text-gray-600 font-mono">/api/dynamic/openapi.json</span>
                  </div>
                  <pre className="flex-1 min-h-0 overflow-auto text-[11px] leading-snug font-mono whitespace-pre-wrap text-gray-300 bg-gray-950/60 rounded p-2">
                    {specText ?? 'Click "Load spec" to fetch the generated OpenAPI 3 document.'}
                  </pre>
                </div>
              )}

              {tab === 'test' && (
                <div className="space-y-2 h-full flex flex-col">
                  <div className="flex items-center gap-1.5 shrink-0">
                    <select className={`${selectCls} w-[74px] shrink-0`} value={testMethod} onChange={(e) => setTestMethod(e.target.value)}>
                      {METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
                    </select>
                    <input className={`${inputCls} flex-1 min-w-0 font-mono`} value={testUrl} onChange={(e) => setTestUrl(e.target.value)} />
                    {draft && draft.operations.length > 0 && (
                      <select className={`${selectCls} max-w-[220px] shrink-0`} title="Prefill method + URL from an operation"
                        value={selectedOpIndex} onChange={(e) => handleOpSelect(Number(e.target.value))}>
                        {draft.operations.map((op, i) => (
                          <option key={i} value={i}>{op.method} {DynamicApiService.joinPaths(draft.basePath || '/', op.path)}</option>
                        ))}
                      </select>
                    )}
                    <button className="btn btn-primary !px-2.5 !py-1 text-xs shrink-0" disabled={sending} onClick={() => void handleSend()}>
                      <Play className="w-3 h-3" /> {sending ? 'Sending…' : 'Send'}
                    </button>
                  </div>
                  <input className={`${inputCls} font-mono shrink-0`} value={authHeader} placeholder="Authorization header (auto-filled from the bearer token)"
                    onChange={(e) => setAuthHeader(e.target.value)} />
                  {testMethod !== 'GET' && (
                    <textarea className={`${inputCls} font-mono shrink-0 resize-y`} rows={2} value={testBodyInput}
                      placeholder="Request body (JSON) - sent with POST/PUT/PATCH when non-empty"
                      onChange={(e) => setTestBodyInput(e.target.value)} />
                  )}
                  {testStatus && (
                    <div className={`text-xs font-mono shrink-0 ${testStatus.startsWith('2') ? 'text-green-400' : testStatus.startsWith('4') || testStatus.startsWith('5') ? 'text-red-400' : 'text-gray-300'}`}>
                      {testStatus}
                    </div>
                  )}
                  <pre className="flex-1 min-h-0 overflow-auto text-[11px] leading-snug font-mono whitespace-pre-wrap text-gray-300 bg-gray-950/60 rounded p-2">
                    {testBody || 'Response body appears here after Send.'}
                  </pre>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
      <AiDynamicApiWizard
        open={wizardOpen}
        initialNodePath={selectedNodePath}
        initialDomain={domainNames[0] ?? ''}
        onClose={() => setWizardOpen(false)}
        onSaved={(id) => { void handleWizardSaved(id); }}
      />
    </div>
  );
}
