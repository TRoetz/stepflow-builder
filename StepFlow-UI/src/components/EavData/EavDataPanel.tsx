import { useCallback, useEffect, useState } from 'react';
import Editor from '@monaco-editor/react';
import { Database, X, Plus, Trash2, AlertCircle, Loader2, ChevronLeft, ChevronRight } from 'lucide-react';
import { EavService, type EavDomain, type EavRowRecord } from '@services/eavService';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// EAV Data Panel — browse and edit captured attribute rows.
// Left: domains with row counts + contract badges. Right: a paged,
// filterable/sortable table (fixed entity columns + dynamic value
// columns), a per-row JSON values editor (PUT / DELETE) and an
// add-row form (POST). No polling — refreshes on selection/mutations.
// ═══════════════════════════════════════════════════════════

const RESERVED_KEYS: Record<string, true> = { rowKeyId: true, entityId: true, entityType: true, sourceTaskId: true, capturedAtUtc: true };
const PAGE_SIZE = 50;
const MAX_DYNAMIC_COLUMNS = 8;

// Parse "key=value,key2=value2" into equality filters (first '=' splits each pair).
function parseFilterText(text: string): Record<string, string> {
  const filter: Record<string, string> = {};
  for (const part of text.split(',')) {
    const idx = part.indexOf('=');
    if (idx <= 0) continue;
    const key = part.slice(0, idx).trim();
    const value = part.slice(idx + 1).trim();
    if (key.length > 0) filter[key] = value;
  }
  return filter;
}

function rowValues(row: EavRowRecord): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(row)) if (!(k in RESERVED_KEYS)) values[k] = v;
  return values;
}

function cellText(value: unknown): string {
  if (value === null || value === undefined) return '—';
  const s = typeof value === 'string' ? value : JSON.stringify(value);
  return s.length > 40 ? `${s.slice(0, 37)}…` : s;
}

export function EavDataPanel({ onClose }: { onClose: () => void }) {
  const [domains, setDomains] = useState<EavDomain[]>([]);
  const [selectedDomain, setSelectedDomain] = useState<string | null>(null);
  const [rows, setRows] = useState<EavRowRecord[]>([]);
  const [count, setCount] = useState(0);
  const [filterText, setFilterText] = useState('');
  const [sort, setSort] = useState('-capturedAtUtc');
  const [page, setPage] = useState(1);
  const [selectedRowKey, setSelectedRowKey] = useState<string | null>(null);
  const [valuesText, setValuesText] = useState('{}');
  const [showAddForm, setShowAddForm] = useState(false);
  const [addEntityId, setAddEntityId] = useState('');
  const [addValuesText, setAddValuesText] = useState('{\n  \n}');
  const [busy, setBusy] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadDomains = useCallback(async (select?: string) => {
    try {
      const list = await EavService.listDomains();
      setDomains(list);
      setLoadError(null);
      setSelectedDomain((prev) => select ?? prev ?? (list.length > 0 ? list[0].domain : null));
    } catch (e) {
      setLoadError((e as Error).message);
    }
  }, []);

  const loadRows = useCallback(async (domain: string, filterTextArg: string, sortArg: string, pageArg: number) => {
    try {
      const result = await EavService.listRows(domain, {
        filter: parseFilterText(filterTextArg),
        sort: sortArg || undefined,
        page: pageArg,
        limit: PAGE_SIZE,
      });
      setRows(result.rows);
      setCount(result.count);
    } catch (e) {
      setLoadError((e as Error).message); // e.g. 400 on an unsafe domain name
    }
  }, []);

  useEffect(() => { void loadDomains(); }, [loadDomains]);

  // Load the first page whenever the domain or query changes.
  useEffect(() => {
    if (!selectedDomain) return;
    setPage(1);
    setSelectedRowKey(null);
    void loadRows(selectedDomain, filterText, sort, 1);
  }, [selectedDomain, filterText, sort, loadRows]);

  const changePage = (delta: number) => {
    if (!selectedDomain || busy) return;
    const next = page + delta;
    if (next < 1 || next > Math.max(1, Math.ceil(count / PAGE_SIZE))) return;
    setPage(next);
    void loadRows(selectedDomain, filterText, sort, next);
  };

  // Dynamic value columns: union of non-reserved keys across the current page.
  const dynamicColumns = Array.from(new Set(rows.flatMap((r) => Object.keys(rowValues(r))))).slice(0, MAX_DYNAMIC_COLUMNS);

  const selectRow = (row: EavRowRecord) => {
    setSelectedRowKey(row.rowKeyId);
    setValuesText(JSON.stringify(rowValues(row), null, 2));
    setShowAddForm(false);
  };

  const handleSaveRow = async () => {
    if (!selectedDomain || !selectedRowKey || busy) return;
    let values: Record<string, unknown>;
    try {
      const parsed = JSON.parse(valuesText || '{}');
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('must be a JSON object');
      values = parsed as Record<string, unknown>;
    } catch (e) {
      showToast({ type: 'error', message: `Invalid values JSON — ${(e as Error).message}` });
      return;
    }
    setBusy(true);
    try {
      await EavService.updateRow(selectedDomain, selectedRowKey, values);
      showToast({ type: 'success', message: `Row ${selectedRowKey.slice(0, 8)} updated` });
      void loadRows(selectedDomain, filterText, sort, page);
    } catch (e) {
      showToast({ type: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  };

  const handleDeleteRow = async () => {
    if (!selectedDomain || !selectedRowKey || busy) return;
    if (!window.confirm(`Delete row ${selectedRowKey} from domain '${selectedDomain}'?`)) return;
    setBusy(true);
    try {
      await EavService.deleteRow(selectedDomain, selectedRowKey);
      showToast({ type: 'success', message: `Row ${selectedRowKey.slice(0, 8)} deleted` });
      setSelectedRowKey(null);
      void loadRows(selectedDomain, filterText, sort, page);
      void loadDomains(); // row counts changed
    } catch (e) {
      showToast({ type: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  };

  const handleAddRow = async () => {
    if (!selectedDomain || busy) return;
    let values: Record<string, unknown>;
    try {
      const parsed = JSON.parse(addValuesText || '{}');
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('must be a JSON object');
      values = parsed as Record<string, unknown>;
    } catch (e) {
      showToast({ type: 'error', message: `Invalid values JSON — ${(e as Error).message}` });
      return;
    }
    setBusy(true);
    try {
      const body: Record<string, unknown> = { ...values };
      if (addEntityId.trim()) body.entityId = addEntityId.trim();
      await EavService.createRow(selectedDomain, body);
      showToast({ type: 'success', message: `Row added to '${selectedDomain}'` });
      setAddEntityId('');
      setAddValuesText('{\n  \n}');
      setShowAddForm(false);
      void loadRows(selectedDomain, filterText, sort, page);
      void loadDomains(); // row counts changed
    } catch (e) {
      showToast({ type: 'error', message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  };

  const totalPages = Math.max(1, Math.ceil(count / PAGE_SIZE));

  return (
    <div className="w-[880px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <Database className="w-4 h-4 text-indigo-400" />
          EAV Data
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title="Close EAV Data Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body: two columns */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left column: domains */}
        <div className="w-[240px] shrink-0 border-r border-gray-800 flex flex-col min-h-0">
          {loadError && (
            <div className="flex items-center gap-2 px-3 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {loadError}
            </div>
          )}
          <div className="px-3 py-2 border-b border-gray-800">
            <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">Domains</span>
          </div>
          <div className="flex-1 overflow-y-auto">
            {domains.length === 0 ? (
              <div className="text-xs text-gray-600 italic px-4 py-6 text-center">No EAV domains yet.</div>
            ) : (
              domains.map((d) => {
                const selected = d.domain === selectedDomain;
                return (
                  <button
                    key={d.domain}
                    onClick={() => setSelectedDomain(d.domain)}
                    className={`w-full text-left px-3 py-2.5 border-b border-gray-800/60 transition-colors ${selected ? 'bg-indigo-600/15' : 'hover:bg-gray-800/40'}`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`text-xs truncate flex-1 font-mono ${selected ? 'text-gray-100' : 'text-gray-300'}`}>{d.domain}</span>
                      <span className="shrink-0 text-[9px] text-gray-600">{d.rows} rows</span>
                    </div>
                    {(d.hasRegistryContract || d.hasAttributeDomain) && (
                      <div className="flex items-center gap-1 mt-1">
                        {d.hasRegistryContract && (
                          <span className="px-1 py-0.5 rounded bg-cyan-500/15 text-cyan-300 text-[7px] font-bold" title="Has a registry entity contract">REG</span>
                        )}
                        {d.hasAttributeDomain && (
                          <span className="px-1 py-0.5 rounded bg-emerald-500/15 text-emerald-300 text-[7px] font-bold" title="Has an attribute domain contract">ATTR</span>
                        )}
                      </div>
                    )}
                  </button>
                );
              })
            )}
          </div>
        </div>

        {/* Right column: rows table + editors */}
        <div className="flex-1 flex flex-col min-h-0">
          {!selectedDomain ? (
            <div className="flex-1 flex items-center justify-center text-xs text-gray-600 italic">Select a domain to browse its rows.</div>
          ) : (
            <>
              {/* Toolbar: filter, sort, pagination */}
              <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-800 flex-wrap">
                <input
                  type="text"
                  value={filterText}
                  onChange={(e) => setFilterText(e.target.value)}
                  placeholder="filter: key=value,key2=value2"
                  className="flex-1 min-w-[180px] px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 font-mono outline-none"
                />
                <select
                  value={sort}
                  onChange={(e) => setSort(e.target.value)}
                  className="px-2 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-300 outline-none"
                >
                  <option value="-capturedAtUtc">newest first</option>
                  <option value="capturedAtUtc">oldest first</option>
                  <option value="entityId">by entityId</option>
                  <option value="">no sort</option>
                </select>
                <div className="flex items-center gap-1 text-xs text-gray-400">
                  <button className="btn-icon" title="Previous page" onClick={() => changePage(-1)} disabled={page <= 1}>
                    <ChevronLeft className="w-3.5 h-3.5" />
                  </button>
                  <span className="text-[10px] whitespace-nowrap">p {page}/{totalPages} · {count}</span>
                  <button className="btn-icon" title="Next page" onClick={() => changePage(1)} disabled={page >= totalPages}>
                    <ChevronRight className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              {/* Rows table */}
              <div className="flex-none max-h-[46%] overflow-auto border-b border-gray-800">
                <table className="w-full text-left">
                  <thead className="sticky top-0 bg-gray-900 z-10">
                    <tr className="text-[9px] uppercase tracking-wider text-gray-600">
                      <th className="px-3 py-2 font-semibold">EntityId</th>
                      <th className="px-3 py-2 font-semibold">Type</th>
                      {dynamicColumns.map((c) => (
                        <th key={c} className="px-3 py-2 font-semibold" title={c}>{c}</th>
                      ))}
                      <th className="px-3 py-2 font-semibold text-right">Captured</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.length === 0 ? (
                      <tr><td colSpan={4 + dynamicColumns.length} className="px-4 py-6 text-center text-xs text-gray-600 italic">No rows{filterText ? ' match the filter' : ''}.</td></tr>
                    ) : (
                      rows.map((r) => {
                        const selected = r.rowKeyId === selectedRowKey;
                        return (
                          <tr
                            key={r.rowKeyId}
                            onClick={() => selectRow(r)}
                            className={`cursor-pointer border-t border-gray-800/60 transition-colors ${selected ? 'bg-indigo-600/15' : 'hover:bg-gray-800/40'}`}
                          >
                            <td className="px-3 py-2 text-[10px] font-mono text-gray-300 whitespace-nowrap" title={String(r.entityId ?? '')}>
                              {cellText(r.entityId)}
                            </td>
                            <td className="px-3 py-2 text-[10px] text-gray-500 whitespace-nowrap">{cellText(r.entityType)}</td>
                            {dynamicColumns.map((c) => (
                              <td key={c} className="px-3 py-2 text-[10px] font-mono text-gray-400 max-w-[160px]" title={JSON.stringify(rowValues(r)[c])}>
                                {cellText(rowValues(r)[c])}
                              </td>
                            ))}
                            <td className="px-3 py-2 text-[9px] text-gray-600 whitespace-nowrap text-right">
                              {r.capturedAtUtc ? new Date(String(r.capturedAtUtc)).toLocaleString() : '—'}
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>

              {/* Row editor / add form */}
              <div className="flex-1 overflow-y-auto p-3 min-h-[200px]">
                {selectedRowKey ? (
                  <div className="space-y-2 max-w-[760px]">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">
                        Row values — <span className="font-mono normal-case">{selectedRowKey}</span>
                      </span>
                      <div className="flex items-center gap-1.5">
                        <button className="btn btn-primary gap-1.5 text-xs disabled:opacity-50" onClick={handleSaveRow} disabled={busy}>
                          {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null} Save Values (PUT)
                        </button>
                        <button className="btn gap-1.5 text-xs disabled:opacity-50" onClick={handleDeleteRow} disabled={busy}>
                          <Trash2 className="w-3.5 h-3.5 text-red-400" /> Delete Row
                        </button>
                      </div>
                    </div>
                    <Editor
                      height={180}
                      language="json"
                      theme="vs-dark"
                      value={valuesText}
                      onChange={(v) => setValuesText(v ?? '{}')}
                      options={{ minimap: { enabled: false }, fontSize: 11, scrollBeyondLastLine: false }}
                    />
                  </div>
                ) : showAddForm ? (
                  <div className="space-y-2 max-w-[760px]">
                    <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block">New row in '{selectedDomain}'</span>
                    <input
                      type="text"
                      value={addEntityId}
                      onChange={(e) => setAddEntityId(e.target.value)}
                      placeholder="entityId (optional)"
                      className="w-64 px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 font-mono outline-none"
                    />
                    <Editor
                      height={160}
                      language="json"
                      theme="vs-dark"
                      value={addValuesText}
                      onChange={(v) => setAddValuesText(v ?? '{}')}
                      options={{ minimap: { enabled: false }, fontSize: 11, scrollBeyondLastLine: false }}
                    />
                    <button className="btn btn-primary gap-1.5 text-xs disabled:opacity-50" onClick={handleAddRow} disabled={busy}>
                      {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Plus className="w-3.5 h-3.5" />} Add Row (POST)
                    </button>
                  </div>
                ) : (
                  <div className="flex items-center gap-2">
                    <button className="btn gap-1.5 text-xs" onClick={() => setShowAddForm(true)}>
                      <Plus className="w-3.5 h-3.5" /> Add Row
                    </button>
                    <span className="text-[10px] text-gray-600 italic">Click a row above to edit its values.</span>
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
