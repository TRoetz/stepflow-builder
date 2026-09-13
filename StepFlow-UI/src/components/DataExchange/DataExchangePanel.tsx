import { useState, useCallback, useEffect } from 'react';
import Editor from '@monaco-editor/react';
import {
  ArrowLeftRight,
  Maximize2,
  Minimize2,
  X,
  Search,
  Plus,
  Trash2,
  Play,
  Save,
  Activity,
  FileJson,
  CheckCircle2,
  AlertCircle,
  Sparkles,
  Wand2,
} from 'lucide-react';
import { DataExchangeService, type DataExchangeProfile, type ExecutionRecord, type ProfileListItem } from '@services/dataExchangeService';
import { PipelineVisualizer } from './PipelineVisualizer';
import { SchemaEditor } from './SchemaEditor';
import { ProfileWizard } from './ProfileWizard';
import { AiBuildModal } from './AiBuildModal';
import { showToast } from '@stores/useToastStore';

type View = 'profiles' | 'monitor';

const NEW_PROFILE_TEMPLATE = JSON.stringify(
  {
    dataExchangeProfileName: 'New Profile',
    isActive: true,
    dataSource: { mediumType: 3 },
    pipeline: { pipelineStages: [] }
  },
  null,
  2
);

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
function isProfile(value: unknown): value is DataExchangeProfile {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const record = value as Record<string, unknown>; // JSON.parse boundary — shape checked below
  return typeof record.dataExchangeProfileName === 'string' && record.dataExchangeProfileName.length > 0;
}

export function DataExchangePanel({ onClose }: { onClose: () => void }) {
  const [view, setView] = useState<View>('profiles');

  // ── Profiles state ────────────────────────────────────────────────────────
  const [profiles, setProfiles] = useState<ProfileListItem[]>([]);
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [jsonText, setJsonText] = useState(NEW_PROFILE_TEMPLATE);
  const [isDirty, setIsDirty] = useState(false);
  const [busy, setBusy] = useState<'save' | 'run' | null>(null);
  const [lastResult, setLastResult] = useState<string | null>(null);

  // ── Visual editing (schema editor / AI build) ─────────────────────────────
  const [editorMode, setEditorMode] = useState<'json' | 'schema'>('json');
  const [aiBuildOpen, setAiBuildOpen] = useState(false);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [wizardInitial, setWizardInitial] = useState<DataExchangeProfile | null>(null);

  // ── Monitor state ─────────────────────────────────────────────────────────
  const [executions, setExecutions] = useState<ExecutionRecord[]>([]);
  const [expandedExecutionId, setExpandedExecutionId] = useState<string | null>(null);

  // ── Panel layout ──────────────────────────────────────────────────────────
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    if (!expanded) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setExpanded(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [expanded]);

  const loadProfiles = useCallback(async () => {
    try {
      const list = await DataExchangeService.listProfiles();
      setProfiles(list);
    } catch (err) {
      showToast({ type: 'error', message: `Failed to load profiles: ${errorMessage(err)}` });
    }
  }, []);

  useEffect(() => {
    void loadProfiles();
  }, [loadProfiles]);

  const selectedProfile = profiles.find((p) => p.id === selectedId) ?? null;

  // ── Monitor polling (only while the monitor view is open) ─────────────────
  useEffect(() => {
    if (view !== 'monitor') return;
    let cancelled = false;
    const poll = async () => {
      try {
        const list = await DataExchangeService.listExecutions(50);
        if (!cancelled) setExecutions(list);
      } catch {
        /* transient — retry on next tick */
      }
    };
    void poll();
    const timer = setInterval(poll, 5000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [view]);

  // ── Profile actions ───────────────────────────────────────────────────────
  const handleSelectProfile = useCallback(
    (item: ProfileListItem) => {
      setSelectedId(item.id);
      setJsonText(JSON.stringify(item.profile, null, 2));
      setIsDirty(false);
      setLastResult(null);
    },
    []
  );

  const handleNewProfile = useCallback(() => {
    setSelectedId(null);
    setJsonText(NEW_PROFILE_TEMPLATE);
    setIsDirty(true);
    setLastResult(null);
  }, []);

  const applyGeneratedJson = useCallback((text: string) => {
    setJsonText(text);
    setIsDirty(true);
    setEditorMode('schema'); // show the generated structure immediately
  }, []);

  const handleDeleteProfile = useCallback(
    async (profile: ProfileListItem) => {
      if (!profile.id) return;
      if (!window.confirm(`Delete profile "${profile.name}"?`)) return;
      try {
        await DataExchangeService.deleteProfile(profile.id);
        showToast({ type: 'success', message: `Profile "${profile.name}" deleted` });
        if (selectedId === profile.id) handleNewProfile();
        void loadProfiles();
      } catch (err) {
        showToast({ type: 'error', message: `Delete failed: ${errorMessage(err)}` });
      }
    },
    [selectedId, handleNewProfile, loadProfiles]
  );

  const parseEditorJson = useCallback((): DataExchangeProfile | null => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(jsonText);
    } catch (err) {
      showToast({ type: 'error', message: `Invalid JSON: ${errorMessage(err)}` });
      return null;
    }
    if (!isProfile(parsed)) {
      const isObject = typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed);
      showToast({
        type: 'error',
        message: isObject ? 'Profile requires a "dataExchangeProfileName" string field' : 'Profile must be a JSON object with a dataExchangeProfileName',
      });
      return null;
    }
    return parsed;
  }, [jsonText]);
  // ── Guided editing (wizard) ───────────────────────────────────────────────
  const openNewWizard = useCallback(() => {
    setSelectedId(null);
    setWizardInitial(null);
    setWizardOpen(true);
  }, []);

  const openWizardForSelected = useCallback(() => {
    const profile = parseEditorJson();
    if (!profile) return;
    setWizardInitial(profile);
    setWizardOpen(true);
  }, [parseEditorJson]);

  const handleWizardSave = useCallback(
    async (doc: DataExchangeProfile) => {
      try {
        const saved = await DataExchangeService.saveProfile(doc);
        showToast({ type: 'success', message: `Profile "${doc.dataExchangeProfileName}" saved` });
        setSelectedId(typeof saved?.id === 'string' ? saved.id : null);
        setJsonText(JSON.stringify(doc, null, 2));
        setIsDirty(false);
        setLastResult(null);
        setWizardOpen(false);
        void loadProfiles();
      } catch (err) {
        showToast({ type: 'error', message: `Save failed: ${errorMessage(err)}` });
      }
    },
    [loadProfiles]
  );

  const handleSave = useCallback(async () => {
    const profile = parseEditorJson();
    if (!profile) return;
    setBusy('save');
    try {
      const saved = await DataExchangeService.saveProfile(profile);
      showToast({ type: 'success', message: `Profile "${profile.dataExchangeProfileName}" saved` });
      setSelectedId(saved.id ?? null);
      setIsDirty(false);
      void loadProfiles();
    } catch (err) {
      showToast({ type: 'error', message: `Save failed: ${errorMessage(err)}` });
    } finally {
      setBusy(null);
    }
  }, [parseEditorJson, loadProfiles]);

  const handleRun = useCallback(async () => {
    if (!selectedProfile?.id) {
      showToast({ type: 'error', message: 'Save the profile before running it' });
      return;
    }
    setBusy('run');
    try {
      const result = await DataExchangeService.execute(selectedProfile.id);
      setLastResult(JSON.stringify(result, null, 2));
      showToast({ type: 'success', message: `Execution finished for "${selectedProfile.name}"` });
    } catch (err) {
      showToast({ type: 'error', message: `Execution failed: ${errorMessage(err)}` });
    } finally {
      setBusy(null);
    }
  }, [selectedProfile]);

  const filteredProfiles = profiles.filter(
    (p) => !filter || p.name.toLowerCase().includes(filter.toLowerCase())
  );

  return (
    <div className={expanded ? 'fixed inset-0 z-50 flex flex-col bg-gray-900' : 'w-[560px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800'}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <div className="flex items-center gap-2">
          <ArrowLeftRight className="w-4 h-4 text-indigo-400" />
          <span className="text-sm font-semibold text-gray-200">Data Exchange</span>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={() => setExpanded((v) => !v)}
            title={expanded ? 'Restore panel (Esc)' : 'Expand to full canvas'}
            className="p-1 rounded-md hover:bg-gray-800 text-gray-400 hover:text-gray-200 transition-colors"
          >
            {expanded ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
          </button>
          <button
            onClick={onClose}
            className="p-1 rounded-md hover:bg-gray-800 text-gray-400 hover:text-gray-200 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* View switcher */}
      <div className="flex gap-1 px-4 py-2 border-b border-gray-800">
        {(
          [
            ['profiles', 'Profiles'],
            ['monitor', 'Monitor'],
          ] as Array<[View, string]>
        ).map(([key, label]) => (
          <button
            key={key}
            onClick={() => setView(key)}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${
              view === key
                ? 'bg-indigo-600/20 text-indigo-300'
                : 'text-gray-400 hover:text-gray-200 hover:bg-gray-800'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {view === 'profiles' ? (
        <div className="flex-1 flex overflow-hidden">
          {/* Left: profile list */}
          <div className="w-52 shrink-0 border-r border-gray-800 flex flex-col">
            <div className="p-3 space-y-2 border-b border-gray-800/60">
              <div className="relative">
                <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-gray-500" />
                <input
                  type="text"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  placeholder="Search profiles..."
                  className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg pl-9 pr-3 py-2 text-sm text-gray-200 placeholder-gray-500 focus:outline-none focus:border-indigo-500/50 transition-colors"
                />
              </div>
              <button
                onClick={openNewWizard}
                className="w-full flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition-colors"
              >
                <Plus className="w-3.5 h-3.5" />
                New Profile
              </button>
              <button
                onClick={() => setAiBuildOpen(true)}
                className="w-full flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg border border-indigo-500/40 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-300 text-xs font-semibold transition-colors"
              >
                <Sparkles className="w-3.5 h-3.5" />
                AI Build
              </button>
            </div>
            <div className="flex-1 overflow-y-auto">
              {filteredProfiles.length === 0 ? (
                <div className="text-xs text-gray-500 text-center py-8 px-4">
                  No profiles yet. Create one to map an external file schema into your internal model.
                </div>
              ) : (
                filteredProfiles.map((item) => {
                  const isSelected = selectedId === item.id;
                  const sourceName = item.profile.dataSource?.dataSourceName;
                  return (
                    <button
                      key={item.id}
                      onClick={() => handleSelectProfile(item)}
                      className={`w-full text-left px-3 py-2.5 flex items-center justify-between gap-2 border-b border-gray-800/40 transition-colors ${
                        isSelected ? 'bg-indigo-600/10' : 'hover:bg-gray-800/40'
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="text-xs font-semibold text-gray-200 truncate">{item.name}</div>
                        {typeof sourceName === 'string' ? (
                          <div className="text-[10px] text-gray-500 font-mono truncate">
                            source: {sourceName}
                          </div>
                        ) : null}
                      </div>
                      <span
                        role="button"
                        tabIndex={0}
                        title="Delete profile"
                        onClick={(e) => {
                          e.stopPropagation();
                          void handleDeleteProfile(item);
                        }}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.stopPropagation();
                            void handleDeleteProfile(item);
                          }
                        }}
                        className="p-1 rounded text-gray-500 hover:text-red-400 hover:bg-red-500/10 transition-colors"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </span>
                    </button>
                  );
                })
              )}
            </div>
          </div>

          {/* Right: profile editor */}
          <div className="flex-1 flex flex-col overflow-hidden">
            {selectedProfile || isDirty ? (
              <>
                <div className="px-3 py-2 border-b border-gray-800/60 flex items-center gap-2">
                  <FileJson className="w-4 h-4 text-indigo-400 shrink-0" />
                  <span className="text-xs font-semibold text-gray-200 truncate">{selectedProfile?.name ?? 'New Profile'}</span>
                  {isDirty && (
                    <span className="text-[9px] px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-400 font-bold shrink-0">UNSAVED</span>
                  )}
                  <div className="ml-auto flex items-center gap-1.5">
                    {selectedProfile && (
                      <button
                        onClick={() => setAiBuildOpen(true)}
                        title="Refine this profile with AI"
                        className="flex items-center gap-1 px-2 py-1 rounded-lg border border-indigo-500/40 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-300 text-[10px] font-semibold transition-colors"
                      >
                        <Sparkles className="w-3 h-3" />
                        AI Build
                      </button>
                    )}
                    <button
                      onClick={openWizardForSelected}
                      title="Edit this profile in the step-by-step wizard"
                      className="flex items-center gap-1 px-2 py-1 rounded-lg border border-gray-600/60 bg-gray-800/60 hover:bg-gray-700/60 text-gray-300 text-[10px] font-semibold transition-colors"
                    >
                      <Wand2 className="w-3 h-3" />
                      Wizard
                    </button>
                    <div className="flex rounded-lg border border-gray-700/60 overflow-hidden">
                      {(['json', 'schema'] as const).map((mode) => (
                        <button
                          key={mode}
                          onClick={() => setEditorMode(mode)}
                          title={mode === 'json' ? 'Edit raw JSON' : 'Visual schema editing'}
                          className={`px-2 py-1 text-[10px] font-semibold transition-colors ${editorMode === mode ? 'bg-indigo-600 text-white' : 'text-gray-400 hover:text-gray-200'}`}
                        >
                          {mode.toUpperCase()}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
                <div className="max-h-40 overflow-y-auto border-b border-gray-800/60 px-3 py-2">
                  <PipelineVisualizer jsonText={jsonText} />
                </div>
                {editorMode === 'schema' ? (
                  <SchemaEditor
                    jsonText={jsonText}
                    onChange={(text) => {
                      setJsonText(text);
                      setIsDirty(true);
                    }}
                  />
                ) : (
                  <div className="flex-1 min-h-0">
                    <Editor
                      key={selectedId ?? 'new'}
                      height="100%"
                      defaultLanguage="json"
                      theme="vs-dark"
                      value={jsonText}
                      onChange={(value) => {
                        setJsonText(value ?? '');
                        setIsDirty(true);
                      }}
                      options={{
                        minimap: { enabled: false },
                        fontSize: 12,
                        scrollBeyondLastLine: false,
                        automaticLayout: true,
                        tabSize: 2,
                      }}
                    />
                  </div>
                )}
                <div className="px-3 py-2 border-t border-gray-800 flex items-center gap-2">
                  <button
                    onClick={() => void handleSave()}
                    disabled={busy !== null}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white text-xs font-semibold transition-colors"
                  >
                    <Save className="w-3.5 h-3.5" />
                    Save
                  </button>
                  <button
                    onClick={() => void handleRun()}
                    disabled={busy !== null || !selectedProfile?.id}
                    title={selectedProfile?.id ? 'Execute this profile now' : 'Save the profile first'}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 disabled:opacity-40 text-white text-xs font-semibold transition-colors"
                  >
                    <Play className="w-3.5 h-3.5" />
                    Run
                  </button>
                  {busy && (
                    <span className="text-[10px] text-gray-400">{busy === 'run' ? 'Executing…' : 'Saving…'}</span>
                  )}
                </div>
              </>
            ) : (
              <div className="flex-1 flex items-center justify-center px-6">
                <div className="space-y-2 text-center">
                  <p className="text-xs text-gray-500 leading-relaxed">
                    Select a profile on the left, or create a new one. Profiles define how an external file schema is
                    mapped into your internal model and enriched via lookups before downstream distribution.
                  </p>
                  <button
                    onClick={() => setAiBuildOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-indigo-500/40 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-300 text-xs font-semibold transition-colors"
                  >
                    <Sparkles className="w-3.5 h-3.5" />
                    Build one with AI
                  </button>
                  <p className="text-[10px] text-gray-600">
                    Invoke from a flow with Resource{' '}
                    <code className="font-mono text-indigo-300/80">dataexchange://&lt;profileId&gt;</code>
                  </p>
                </div>
              </div>
            )}

            {/* Execution result */}
            {lastResult && (
              <div className="border-t border-gray-800 max-h-48 overflow-y-auto p-3 bg-black/30">
                <div className="text-[10px] text-gray-500 uppercase tracking-wider font-semibold mb-2">
                  Last Execution Result
                </div>
                <pre className="font-mono text-[10px] leading-normal text-gray-300 whitespace-pre-wrap break-all">
                  {lastResult}
                </pre>
              </div>
            )}
          </div>
        </div>
      ) : (
        /* ── Monitor view: file inbox execution log ─────────────────────── */
        <div className="flex-1 overflow-y-auto">
          {executions.length === 0 ? (
            <div className="text-xs text-gray-500 text-center py-12 px-6 leading-relaxed">
              No executions recorded yet. Drop a file into the inbox folder of an active File-based profile and it
              will appear here within one poll interval.
            </div>
          ) : (
            executions.map((exec, i) => {
              const isExpanded = expandedExecutionId === exec.executionId;
              return (
                <div key={exec.executionId ?? i} className="border-b border-gray-800/40">
                  <button
                    onClick={() => setExpandedExecutionId(isExpanded ? null : exec.executionId ?? null)}
                    className="w-full text-left px-4 py-2.5 flex items-center gap-3 hover:bg-gray-800/40 transition-colors"
                  >
                    {exec.success ? (
                      <CheckCircle2 className="w-4 h-4 text-green-400 shrink-0" />
                    ) : (
                      <AlertCircle className="w-4 h-4 text-red-400 shrink-0" />
                    )}
                    <div className="min-w-0 flex-1">
                      <div className="text-xs font-semibold text-gray-200 truncate">{exec.profileId ?? 'unknown profile'}</div>
                      <div className="text-[10px] text-gray-500 font-mono mt-0.5 truncate">
                        {typeof exec.rowsIn === 'number' && typeof exec.rowsOut === 'number' ? `${exec.rowsIn} → ${exec.rowsOut} rows` : ''}
                        {typeof exec.rejectedCount === 'number' && exec.rejectedCount > 0 ? ` · ${exec.rejectedCount} rejected` : ''}
                        {(exec.source ?? exec.sourceFile) ? ` · ${exec.source ?? exec.sourceFile}` : ''}
                      </div>
                    </div>
                    <span className="text-[9px] font-bold px-1.5 py-0.5 rounded shrink-0 bg-gray-800 text-gray-400">
                      {isExpanded ? 'HIDE' : 'DETAIL'}
                    </span>
                  </button>
                  {isExpanded && (
                    <div className="px-4 pb-3 space-y-2">
                      {exec.error && (
                        <div className="border border-red-500/20 bg-red-500/5 rounded-lg p-2.5 text-red-400 text-xs whitespace-pre-wrap">
                          {exec.error}
                        </div>
                      )}
                      <pre className="font-mono text-[10px] leading-normal text-gray-300 bg-black/30 border border-gray-800 rounded-lg p-2.5 max-h-64 overflow-y-auto whitespace-pre-wrap break-all">
                        {JSON.stringify(exec, null, 2)}
                      </pre>
                    </div>
                  )}
                </div>
              );
            })
          )}
        </div>
      )}

      {/* Footer hint */}
      <div className="px-4 py-1.5 border-t border-gray-800 flex items-center gap-2 text-[10px] text-gray-600">
        <Activity className="w-3 h-3" />
        {view === 'monitor' ? 'Polling /api/data-exchange/executions every 5s' : `${profiles.length} profile(s) on file`}
      </div>
          <ProfileWizard
            open={wizardOpen}
            initial={wizardInitial}
            onClose={() => setWizardOpen(false)}
            onSave={handleWizardSave}
          />
      <AiBuildModal
        open={aiBuildOpen}
        baseProfile={selectedProfile?.profile ?? null}
        onClose={() => setAiBuildOpen(false)}
        onGenerated={(text) => {
          applyGeneratedJson(text);
          showToast({ type: 'success', message: 'AI draft generated — review, then Save' });
        }}
      />
    </div>
  );
}
