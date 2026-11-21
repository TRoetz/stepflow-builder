import { useCallback, useEffect, useRef, useState } from 'react';
import Editor from '@monaco-editor/react';
import { ClipboardCheck, X, ExternalLink, Loader2, AlertCircle } from 'lucide-react';
import { HumanTaskService, type HumanTask } from '@services/humanTaskService';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// Human Tasks Panel — inbox for stepflow:human:task states.
// Lists pending/completed tasks from the backend, shows each task's
// payload and lets a human complete it (JSON result editor, or the
// standalone form-capture page when completionType is 'form').
// Polls every 5 s while open so new tasks appear without reloading.
// ═══════════════════════════════════════════════════════════

type Tab = 'Pending' | 'Completed' | 'All';
const TABS: Tab[] = ['Pending', 'Completed', 'All'];

function elapsedSince(iso?: string): string {
  if (!iso) return '';
  const ms = Date.now() - new Date(iso).getTime();
  if (Number.isNaN(ms)) return '';
  const m = Math.floor(ms / 60000);
  if (m < 1) return 'just now';
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

function statusBadge(status: string): string {
  switch (status) {
    case 'Pending': return 'bg-amber-500/15 text-amber-300';
    case 'Completed': return 'bg-green-500/15 text-green-400';
    default: return 'bg-gray-700/60 text-gray-400';
  }
}

// JSON block with title bar (same look as PropertyPanel's debug inspector).
function JsonBlock({ title, value }: { title: string; value: unknown }) {
  const empty = value === undefined || value === null;
  return (
    <div className="rounded-lg bg-gray-900/60 border border-gray-800 overflow-hidden">
      <div className="px-2 py-1 border-b border-gray-800/60">
        <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">{title}</span>
      </div>
      <pre className={`text-[10px] font-mono p-2 overflow-auto max-h-48 whitespace-pre-wrap break-words ${empty ? 'text-gray-600 italic' : 'text-gray-300'}`}>
        {empty ? '(empty)' : JSON.stringify(value, null, 2)}
      </pre>
    </div>
  );
}

export function HumanTasksPanel({ onClose }: { onClose: () => void }) {
  const [tab, setTab] = useState<Tab>('Pending');
  const [tasks, setTasks] = useState<HumanTask[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<HumanTask | null>(null);
  const [resultText, setResultText] = useState('{}');
  const [completing, setCompleting] = useState(false);
  const [completeError, setCompleteError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const selectedIdRef = useRef<string | null>(null);
  useEffect(() => { selectedIdRef.current = selectedId; }, [selectedId]);

  // Poll the list (current tab) and the selected task's detail every 5 s while open.
  useEffect(() => {
    let disposed = false;
    const loadList = async () => {
      try {
        const list = await HumanTaskService.list(tab === 'All' ? undefined : tab);
        if (disposed) return;
        setTasks(list);
        setLoadError(null);
        setSelectedId((prev) => prev ?? (list.length > 0 ? list[0].taskId : null));
      } catch (e) {
        if (!disposed) setLoadError((e as Error).message);
      }
    };
    const loadDetail = async () => {
      const id = selectedIdRef.current;
      if (!id) return;
      try {
        const t = await HumanTaskService.get(id);
        if (disposed || selectedIdRef.current !== id) return;
        setDetail(t);
      } catch { /* keep the previous detail on transient errors */ }
    };
    void loadList();
    void loadDetail();
    const timer = setInterval(() => { void loadList(); void loadDetail(); }, 5000);
    return () => { disposed = true; clearInterval(timer); };
  }, [tab]);

  // Fetch the full detail (payload + result) when the selection changes.
  useEffect(() => {
    if (!selectedId) { setDetail(null); setResultText('{}'); setCompleteError(null); return; }
    let cancelled = false;
    HumanTaskService.get(selectedId)
      .then((t) => { if (!cancelled) { setDetail(t); setResultText('{}'); setCompleteError(null); } })
      .catch(() => { /* covered by the polling pass */ });
    return () => { cancelled = true; };
  }, [selectedId]);

  const switchTab = useCallback((t: Tab) => {
    setTab(t);
    setSelectedId(null);
    setDetail(null);
  }, []);

  const handleComplete = async () => {
    if (!detail || completing) return;
    let parsed: unknown;
    try {
      parsed = JSON.parse(resultText || '{}');
    } catch (e) {
      setCompleteError(`Invalid JSON — ${(e as Error).message}`);
      return;
    }
    setCompleting(true);
    setCompleteError(null);
    try {
      await HumanTaskService.complete(detail.taskId, parsed);
      showToast({ type: 'success', message: `Human task ${detail.taskId.slice(0, 8)} completed` });
      setResultText('{}');
      const list = await HumanTaskService.list(tab === 'All' ? undefined : tab);
      setTasks(list);
      try {
        const t = await HumanTaskService.get(detail.taskId);
        setDetail(t);
      } catch {
        // Task left the current tab's view (e.g. Completed filtered out) — clear selection.
        setSelectedId(null);
        setDetail(null);
      }
    } catch (e) {
      // 409 Conflict (already completed) and other backend errors surface here.
      setCompleteError((e as Error).message);
    } finally {
      setCompleting(false);
    }
  };

  return (
    <div className="w-[880px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <ClipboardCheck className="w-4 h-4 text-indigo-400" />
          Human Tasks
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title="Close Human Tasks Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body: two columns */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left column: status tabs + task list */}
        <div className="w-[280px] shrink-0 border-r border-gray-800 flex flex-col min-h-0">
          {loadError && (
            <div className="flex items-center gap-2 px-4 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {loadError}
            </div>
          )}
          {/* Status tabs */}
          <div className="flex items-center gap-1 px-3 py-2 border-b border-gray-800">
            {TABS.map((t) => (
              <button
                key={t}
                onClick={() => switchTab(t)}
                className={`px-2.5 py-1 rounded-md text-[10px] font-semibold transition-all ${
                  tab === t ? 'bg-indigo-600 text-white shadow-sm' : 'text-gray-400 hover:text-gray-200 bg-gray-800/60'
                }`}
              >
                {t}
              </button>
            ))}
          </div>

          {/* Task rows */}
          <div className="flex-1 overflow-y-auto">
            {tasks.length === 0 ? (
              <div className="text-xs text-gray-600 italic px-4 py-6 text-center">No {tab.toLowerCase()} human tasks.</div>
            ) : (
              tasks.map((t) => {
                const selected = t.taskId === selectedId;
                return (
                  <button
                    key={t.taskId}
                    onClick={() => setSelectedId(t.taskId)}
                    className={`w-full text-left px-3 py-2.5 border-b border-gray-800/60 transition-colors ${
                      selected ? 'bg-indigo-600/15' : 'hover:bg-gray-800/40'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`text-xs truncate flex-1 ${selected ? 'text-gray-100' : 'text-gray-300'}`}>
                        {t.title ?? t.stateName}
                      </span>
                      <span className={`shrink-0 px-1.5 py-0.5 rounded text-[8px] font-bold ${statusBadge(t.status)}`}>
                        {t.status.toUpperCase()}
                      </span>
                    </div>
                    <div className="flex items-center gap-2 mt-1">
                      <span className="text-[10px] text-gray-500 truncate flex-1">{t.stateMachineName}</span>
                      {t.assignee && (
                        <span className="shrink-0 px-1.5 py-0.5 rounded bg-cyan-500/15 text-cyan-300 text-[8px] font-semibold">
                          {t.assignee}
                        </span>
                      )}
                      <span className="shrink-0 text-[9px] text-gray-600">{elapsedSince(t.createdAtUtc)}</span>
                    </div>
                  </button>
                );
              })
            )}
          </div>
        </div>

        {/* Right column: selected task detail */}
        <div className="flex-1 overflow-y-auto p-4 min-h-0">
          {!detail ? (
            <div className="h-full flex items-center justify-center text-xs text-gray-600 italic">
              Select a human task to inspect and complete it.
            </div>
          ) : (
            <div className="space-y-3 max-w-[520px]">
              {/* Title + status */}
              <div className="flex items-start justify-between gap-2">
                <h3 className="text-sm font-semibold text-gray-100">{detail.title ?? detail.stateName}</h3>
                <span className={`shrink-0 px-2 py-0.5 rounded text-[9px] font-bold ${statusBadge(detail.status)}`}>
                  {detail.status.toUpperCase()}
                </span>
              </div>

              {/* Meta */}
              <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-[11px]">
                <div><span className="text-gray-600">Flow:</span> <span className="text-gray-300">{detail.stateMachineName}</span></div>
                <div>
                  <span className="text-gray-600">Execution:</span>{' '}
                  <span className="text-gray-300 font-mono" title={detail.executionId}>{detail.executionId.slice(0, 8)}</span>
                </div>
                {detail.assignee && (
                  <div><span className="text-gray-600">Assignee:</span> <span className="text-gray-300">{detail.assignee}</span></div>
                )}
                {(detail.nextState || detail.isEnd) && (
                  <div>
                    <span className="text-gray-600">Next:</span>{' '}
                    <span className="text-gray-300 font-mono">{detail.isEnd ? '(end)' : detail.nextState}</span>
                  </div>
                )}
                <div><span className="text-gray-600">Created:</span> <span className="text-gray-400">{new Date(detail.createdAtUtc).toLocaleString()}</span></div>
                {detail.completedAtUtc && (
                  <div><span className="text-gray-600">Completed:</span> <span className="text-gray-400">{new Date(detail.completedAtUtc).toLocaleString()}</span></div>
                )}
              </div>

              {/* Payload */}
              <JsonBlock title="Input" value={detail.payload?.input} />
              <JsonBlock title="Task" value={detail.payload?.task} />
              {detail.status === 'Completed' && detail.result !== undefined && (
                <JsonBlock title="Result" value={detail.result} />
              )}

              {/* Completion area */}
              {detail.status === 'Pending' ? (
                detail.completionType === 'form' ? (
                  <div className="rounded-lg border border-gray-800 bg-gray-900/60 p-3 space-y-2">
                    <p className="text-[11px] text-gray-400 leading-normal">
                      This task is completed through a form capture. The standalone page validates the input and submits the result.
                    </p>
                    <button
                      className="btn btn-primary gap-1.5 text-xs"
                      onClick={() => window.open(`/form-capture.html?taskId=${encodeURIComponent(detail.taskId)}`)}
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      Open form capture
                    </button>
                  </div>
                ) : (
                  <div className="space-y-2">
                    <label className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block">Result JSON</label>
                    <Editor
                      height={200}
                      language="json"
                      theme="vs-dark"
                      value={resultText}
                      onChange={(v) => setResultText(v ?? '{}')}
                      options={{ minimap: { enabled: false }, fontSize: 11, lineNumbers: 'off', scrollBeyondLastLine: false }}
                    />
                    {completeError && (
                      <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-red-500/10 border border-red-500/30 text-xs text-red-400">
                        <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {completeError}
                      </div>
                    )}
                    <button
                      className="btn btn-primary gap-1.5 text-xs disabled:opacity-50"
                      onClick={handleComplete}
                      disabled={completing}
                    >
                      {completing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null}
                      Complete Task
                    </button>
                  </div>
                )
              ) : (
                <p className="text-[11px] text-gray-600 italic">Task already completed.</p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
