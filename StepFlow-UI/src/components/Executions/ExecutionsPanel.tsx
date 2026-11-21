import { useEffect, useRef, useState } from 'react';
import { Activity, X, AlertCircle, Loader2, Play, Square, Trash2 } from 'lucide-react';
import { ExecutionService, type BackendExecutionSummary, type BackendExecutionDetail } from '@services/executionService';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// Executions Panel — backend execution history (durable store).
// Lists every stored execution (survives restarts, includes terminal
// history), shows input/output/state-history detail on row click and
// offers Resume / Stop / Delete actions. Polls every 5 s while open.
// ═══════════════════════════════════════════════════════════

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

function statusBadge(status: string): { cls: string; pulse?: boolean } {
  switch (status) {
    case 'Running': return { cls: 'bg-indigo-500/15 text-indigo-300', pulse: true };
    case 'Suspended': return { cls: 'bg-amber-500/15 text-amber-300' };
    case 'Completed': return { cls: 'bg-green-500/15 text-green-400' };
    case 'Failed': return { cls: 'bg-red-500/15 text-red-400' };
    case 'Stopped': return { cls: 'bg-gray-700/60 text-gray-400' };
    default: return { cls: 'bg-gray-700/60 text-gray-400' };
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

export function ExecutionsPanel({ onClose }: { onClose: () => void }) {
  const [executions, setExecutions] = useState<BackendExecutionSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<BackendExecutionDetail | null>(null);
  const [acting, setActing] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const selectedIdRef = useRef<string | null>(null);
  useEffect(() => { selectedIdRef.current = selectedId; }, [selectedId]);

  // Poll the list and the selected execution's detail every 5 s while open.
  useEffect(() => {
    let disposed = false;
    const loadList = async () => {
      try {
        const list = await ExecutionService.listBackendExecutions();
        if (disposed) return;
        setExecutions(list);
        setLoadError(null);
        setSelectedId((prev) => prev ?? (list.length > 0 ? list[0].executionId : null));
      } catch (e) {
        if (!disposed) setLoadError((e as Error).message);
      }
    };
    const loadDetail = async () => {
      const id = selectedIdRef.current;
      if (!id) return;
      try {
        const d = await ExecutionService.getBackendExecution(id);
        if (disposed || selectedIdRef.current !== id) return;
        setDetail(d);
      } catch { /* keep the previous detail on transient errors */ }
    };
    void loadList();
    void loadDetail();
    const timer = setInterval(() => { void loadList(); void loadDetail(); }, 5000);
    return () => { disposed = true; clearInterval(timer); };
  }, []);

  // Fetch the full detail when the selection changes.
  useEffect(() => {
    if (!selectedId) { setDetail(null); setActionError(null); return; }
    let cancelled = false;
    ExecutionService.getBackendExecution(selectedId)
      .then((d) => { if (!cancelled) { setDetail(d); setActionError(null); } })
      .catch(() => { /* covered by the polling pass */ });
    return () => { cancelled = true; };
  }, [selectedId]);

  const refreshAll = async () => {
    try {
      const list = await ExecutionService.listBackendExecutions();
      setExecutions(list);
      if (selectedIdRef.current) {
        try { setDetail(await ExecutionService.getBackendExecution(selectedIdRef.current)); }
        catch { setSelectedId(null); setDetail(null); }
      }
    } catch (e) {
      setLoadError((e as Error).message);
    }
  };

  const handleResume = async () => {
    if (!detail || acting) return;
    setActing(true);
    setActionError(null);
    try {
      await ExecutionService.resumeBackendExecution(detail.executionId);
      showToast({ type: 'success', message: `Execution ${detail.executionId} queued for resume` });
      await refreshAll();
    } catch (e) {
      setActionError((e as Error).message); // surfaces 409 Conflict / 404 messages
    } finally {
      setActing(false);
    }
  };

  const handleStop = async () => {
    if (!detail || acting) return;
    setActing(true);
    setActionError(null);
    try {
      await ExecutionService.stopBackendExecution(detail.executionId);
      showToast({ type: 'info', message: `Stop requested for execution ${detail.executionId}` });
      await refreshAll();
    } catch (e) {
      setActionError((e as Error).message);
    } finally {
      setActing(false);
    }
  };

  const handleDelete = async () => {
    if (!detail || acting) return;
    if (!window.confirm(`Delete execution ${detail.executionId} from the flow-state store? This cannot be undone.`)) return;
    setActing(true);
    setActionError(null);
    try {
      await ExecutionService.deleteBackendExecution(detail.executionId);
      showToast({ type: 'success', message: `Execution ${detail.executionId} deleted` });
      setSelectedId(null);
      setDetail(null);
      const list = await ExecutionService.listBackendExecutions();
      setExecutions(list);
    } catch (e) {
      setActionError((e as Error).message);
    } finally {
      setActing(false);
    }
  };

  return (
    <div className="w-[880px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <Activity className="w-4 h-4 text-indigo-400" />
          Executions
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title="Close Executions Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body: table on top, detail below */}
      <div className="flex flex-col flex-1 overflow-hidden">
        {loadError && (
          <div className="flex items-center gap-2 px-4 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
            <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {loadError}
          </div>
        )}

        {/* Execution table */}
        <div className="flex-none max-h-[42%] overflow-y-auto border-b border-gray-800">
          <table className="w-full text-left">
            <thead className="sticky top-0 bg-gray-900 z-10">
              <tr className="text-[9px] uppercase tracking-wider text-gray-600">
                <th className="px-3 py-2 font-semibold">Execution</th>
                <th className="px-3 py-2 font-semibold">Flow</th>
                <th className="px-3 py-2 font-semibold">Status</th>
                <th className="px-3 py-2 font-semibold">Current State</th>
                <th className="px-3 py-2 font-semibold">Started</th>
                <th className="px-3 py-2 font-semibold text-right">Elapsed</th>
              </tr>
            </thead>
            <tbody>
              {executions.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-4 py-6 text-center text-xs text-gray-600 italic">No stored executions.</td>
                </tr>
              ) : (
                executions.map((e) => {
                  const selected = e.executionId === selectedId;
                  const badge = statusBadge(e.status);
                  return (
                    <tr
                      key={e.executionId}
                      onClick={() => setSelectedId(e.executionId)}
                      className={`cursor-pointer border-t border-gray-800/60 transition-colors ${selected ? 'bg-indigo-600/15' : 'hover:bg-gray-800/40'}`}
                    >
                      <td className="px-3 py-2">
                        <span className={`text-[11px] font-mono ${selected ? 'text-gray-100' : 'text-gray-300'}`} title={e.executionId}>
                          {e.executionId.slice(0, 8)}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-[11px] text-gray-400 truncate max-w-[160px]" title={e.stateMachineName}>
                        {e.stateMachineName}
                      </td>
                      <td className="px-3 py-2">
                        <span className={`inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[8px] font-bold ${badge.cls}`}>
                          {badge.pulse && <span className="w-1 h-1 rounded-full bg-indigo-400 animate-pulse" />}
                          {e.status.toUpperCase()}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-[10px] font-mono text-gray-500 truncate max-w-[140px]" title={e.currentState ?? ''}>
                        {e.currentState ?? '—'}
                      </td>
                      <td className="px-3 py-2 text-[10px] text-gray-500 whitespace-nowrap">
                        {new Date(e.startedAt).toLocaleString()}
                      </td>
                      <td className="px-3 py-2 text-[10px] text-gray-600 text-right whitespace-nowrap">{elapsedSince(e.startedAt)}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        {/* Detail */}
        <div className="flex-1 overflow-y-auto p-4 min-h-0">
          {!detail ? (
            <div className="h-full flex items-center justify-center text-xs text-gray-600 italic">
              Select an execution to inspect its input, output and state history.
            </div>
          ) : (
            <div className="space-y-3 max-w-[780px]">
              {/* Title + actions */}
              <div className="flex items-center justify-between gap-2 flex-wrap">
                <h3 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
                  <span className="font-mono">{detail.executionId}</span>
                  <span className={`px-2 py-0.5 rounded text-[9px] font-bold ${statusBadge(detail.status).cls}`}>
                    {detail.status.toUpperCase()}
                  </span>
                </h3>
                <div className="flex items-center gap-1.5">
                  {detail.status === 'Suspended' && (
                    <button className="btn btn-primary gap-1.5 text-xs disabled:opacity-50" onClick={handleResume} disabled={acting}>
                      {acting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Play className="w-3.5 h-3.5" />}
                      Resume
                    </button>
                  )}
                  {detail.status === 'Running' && (
                    <button className="btn gap-1.5 text-xs disabled:opacity-50" onClick={handleStop} disabled={acting}>
                      {acting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Square className="w-3.5 h-3.5" />}
                      Stop
                    </button>
                  )}
                  <button className="btn gap-1.5 text-xs disabled:opacity-50" onClick={handleDelete} disabled={acting}>
                    <Trash2 className="w-3.5 h-3.5 text-red-400" />
                    Delete
                  </button>
                </div>
              </div>

              {/* Meta */}
              <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-[11px]">
                <div><span className="text-gray-600">Flow:</span> <span className="text-gray-300">{detail.stateMachineName}</span></div>
                <div>
                  <span className="text-gray-600">Current state:</span>{' '}
                  <span className="text-gray-300 font-mono">{detail.currentNode ?? '—'}</span>
                </div>
                <div><span className="text-gray-600">Started:</span> <span className="text-gray-400">{new Date(detail.startedAt).toLocaleString()}</span></div>
                {detail.completedAt ? (
                  <div><span className="text-gray-600">Completed:</span> <span className="text-gray-400">{new Date(detail.completedAt).toLocaleString()}</span></div>
                ) : (
                  <div><span className="text-gray-600">Checkpointed:</span> <span className="text-gray-400">{new Date(detail.checkpointedAt).toLocaleString()}</span></div>
                )}
              </div>

              {/* Error banner */}
              {detail.errorMessage && (
                <div className="rounded-lg bg-red-500/10 border border-red-500/30 p-3 space-y-1">
                  <div className="flex items-center gap-2 text-xs font-semibold text-red-400">
                    <AlertCircle className="w-3.5 h-3.5" />
                    {detail.errorCode ? `${detail.errorCode}: ` : ''}{detail.errorMessage}
                  </div>
                </div>
              )}

              {/* Input / output */}
              <JsonBlock title="Input" value={detail.input} />
              <JsonBlock title="Output" value={detail.output} />

              {/* State history timeline */}
              {detail.history && detail.history.length > 0 && (
                <div className="rounded-lg bg-gray-900/60 border border-gray-800 overflow-hidden">
                  <div className="px-2 py-1 border-b border-gray-800/60">
                    <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">State History ({detail.history.length})</span>
                  </div>
                  <div className="max-h-56 overflow-y-auto p-2 space-y-1.5">
                    {detail.history.map((h, i) => (
                      <div key={i} className="text-[10px] font-mono">
                        <span className="text-gray-600">{new Date(h.timestamp).toLocaleTimeString()}</span>{' '}
                        <span className="text-indigo-300 uppercase">{h.type}</span>
                        {h.state && <span className="text-gray-400"> → {h.state}</span>}
                        {h.data !== undefined && h.data !== null && (
                          <pre className="mt-0.5 text-[9px] text-gray-500 whitespace-pre-wrap break-words">{JSON.stringify(h.data)}</pre>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Action errors (409 Conflict etc.) */}
              {actionError && (
                <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-red-500/10 border border-red-500/30 text-xs text-red-400">
                  <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {actionError}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
