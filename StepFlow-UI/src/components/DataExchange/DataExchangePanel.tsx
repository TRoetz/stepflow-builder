import { useState, useCallback, useEffect } from 'react';
import Editor from '@monaco-editor/react';
import {
  ArrowLeftRight,
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
} from 'lucide-react';
import { DataExchangeService, type DataExchangeProfile, type ExecutionRecord } from '@services/dataExchangeService';
import { showToast } from '@stores/useToastStore';

type View = 'profiles' | 'monitor';

const NEW_PROFILE_TEMPLATE = JSON.stringify(
  { name: 'New Profile', description: '', dataSourceId: null, pipeline: [] },
  null,
  2
);

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
function isProfile(value: unknown): value is DataExchangeProfile {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false;
  const record = value as Record<string, unknown>; // JSON.parse boundary — shape checked below
  return typeof record.name === 'string' && record.name.length > 0;
}

export function DataExchangePanel({ onClose }: { onClose: () => void }) {
  const [view, setView] = useState<View>('profiles');

  // ── Profiles state ────────────────────────────────────────────────────────
  const [profiles, setProfiles] = useState<DataExchangeProfile[]>([]);
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [jsonText, setJsonText] = useState(NEW_PROFILE_TEMPLATE);
  const [isDirty, setIsDirty] = useState(false);
  const [busy, setBusy] = useState<'save' | 'run' | null>(null);
  const [lastResult, setLastResult] = useState<string | null>(null);

  // ── Monitor state ─────────────────────────────────────────────────────────
  const [executions, setExecutions] = useState<ExecutionRecord[]>([]);
  const [expandedExecutionId, setExpandedExecutionId] = useState<string | null>(null);

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
    (profile: DataExchangeProfile) => {
      setSelectedId(profile.id ?? null);
      setJsonText(JSON.stringify(profile, null, 2));
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

  const handleDeleteProfile = useCallback(
    async (profile: DataExchangeProfile) => {
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
        message: isObject ? 'Profile requires a "name" string field' : 'Profile must be a JSON object with a name',
      });
      return null;
    }
    return parsed;
  }, [jsonText]);

  const handleSave = useCallback(async () => {
    const profile = parseEditorJson();
    if (!profile) return;
    setBusy('save');
    try {
      const saved = await DataExchangeService.saveProfile({ ...profile, id: selectedId ?? undefined });
      showToast({ type: 'success', message: `Profile "${saved.name}" saved` });
      setSelectedId(saved.id ?? null);
      setIsDirty(false);
      void loadProfiles();
    } catch (err) {
      showToast({ type: 'error', message: `Save failed: ${errorMessage(err)}` });
    } finally {
      setBusy(null);
    }
  }, [parseEditorJson, selectedId, loadProfiles]);

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
    <div className="w-[560px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <div className="flex items-center gap-2">
          <ArrowLeftRight className="w-4 h-4 text-indigo-400" />
          <span className="text-sm font-semibold text-gray-200">Data Exchange</span>
        </div>
        <button
          onClick={onClose}
          className="p-1 rounded-md hover:bg-gray-800 text-gray-400 hover:text-gray-200 transition-colors"
        >
          <X className="w-4 h-4" />
        </button>
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
                onClick={handleNewProfile}
                className="w-full flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition-colors"
              >
                <Plus className="w-3.5 h-3.5" />
                New Profile
              </button>
            </div>
            <div className="flex-1 overflow-y-auto">
              {filteredProfiles.length === 0 ? (
                <div className="text-xs text-gray-500 text-center py-8 px-4">
                  No profiles yet. Create one to map an external file schema into your internal model.
                </div>
              ) : (
                filteredProfiles.map((profile) => {
                  const isSelected = selectedId === profile.id;
                  return (
                    <button
                      key={profile.id}
                      onClick={() => handleSelectProfile(profile)}
                      className={`w-full text-left px-3 py-2.5 flex items-center justify-between gap-2 border-b border-gray-800/40 transition-colors ${
                        isSelected ? 'bg-indigo-600/10' : 'hover:bg-gray-800/40'
                      }`}
                    >
                      <div className="min-w-0 flex-1">
                        <div className="text-xs font-semibold text-gray-200 truncate">{profile.name}</div>
                        {profile.dataSourceId ? (
                          <div className="text-[10px] text-gray-500 font-mono truncate">
                            source: {String(profile.dataSourceId)}
                          </div>
                        ) : null}
                      </div>
                      <span
                        role="button"
                        tabIndex={0}
                        title="Delete profile"
                        onClick={(e) => {
                          e.stopPropagation();
                          void handleDeleteProfile(profile);
                        }}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.stopPropagation();
                            void handleDeleteProfile(profile);
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
                  <FileJson className="w-3.5 h-3.5 text-indigo-400" />
                  <span className="text-xs font-semibold text-gray-300 truncate">
                    {selectedProfile ? selectedProfile.name : 'New Profile (unsaved)'}
                  </span>
                  {isDirty && <span className="text-[10px] text-amber-400">• unsaved</span>}
                </div>
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
                <p className="text-xs text-gray-500 leading-relaxed">
                  Select a profile on the left, or create a new one. Profiles define how an external file schema is
                  mapped into your internal model and enriched via lookups before downstream distribution.
                </p>
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
            executions.map((exec) => {
              const isExpanded = expandedExecutionId === exec.executionId;
              return (
                <div key={exec.executionId} className="border-b border-gray-800/40">
                  <button
                    onClick={() => setExpandedExecutionId(isExpanded ? null : exec.executionId)}
                    className="w-full text-left px-4 py-2.5 flex items-center gap-3 hover:bg-gray-800/40 transition-colors"
                  >
                    {exec.success ? (
                      <CheckCircle2 className="w-4 h-4 text-green-400 shrink-0" />
                    ) : (
                      <AlertCircle className="w-4 h-4 text-red-400 shrink-0" />
                    )}
                    <div className="min-w-0 flex-1">
                      <div className="text-xs font-semibold text-gray-200 truncate">{exec.profileName}</div>
                      <div className="text-[10px] text-gray-500 font-mono mt-0.5 truncate">
                        {new Date(exec.startedAt).toLocaleString()} · {exec.durationMs}ms
                        {exec.sourceFile ? ` · ${exec.sourceFile}` : ''}
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
                        {JSON.stringify(exec.result ?? null, null, 2)}
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
    </div>
  );
}
