import { useRef, useState } from 'react';
import { Package, Download, Upload, Loader2, AlertCircle } from 'lucide-react';
import { SolutionsService, type SolutionPackage, type SolutionImportReport } from '@services/solutionsService';
import { useWorkspaceStore } from '@stores/useWorkspaceStore';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// Solution Dialog — export/import portable solution packages.
// Export: pick the workspace node (+ optional name/version and SQL/EAV
// seed overrides), download the package JSON and show a section summary.
// Import: load a .json package file, optionally override the target
// node, redeploy idempotently and render the per-section report.
// ═══════════════════════════════════════════════════════════

const DEFAULT_NODE_PATH = 'Default/Default/Default';

const REPORT_SECTIONS: Array<{ key: string; label: string }> = [
  { key: 'flows', label: 'Flows' },
  { key: 'schemaDefinitions', label: 'Schema Definitions' },
  { key: 'attributeDomains', label: 'Attribute Domains' },
  { key: 'forms', label: 'Forms' },
  { key: 'dynamicApis', label: 'Dynamic APIs' },
  { key: 'dataExchangeProfiles', label: 'Data Exchange Profiles' },
  { key: 'rules', label: 'Rules' },
  { key: 'eavEntities', label: 'EAV Entities' },
  { key: 'eavRows', label: 'EAV Rows' },
  { key: 'dataSources', label: 'Data Sources' },
];

function downloadPackage(pkg: SolutionPackage) {
  const name = (pkg.manifest.name || 'solution').replace(/\s+/g, '_');
  const version = pkg.manifest.version || '1';
  const blob = new Blob([JSON.stringify(pkg, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${name}-${version}.json`;
  a.click();
  URL.revokeObjectURL(url);
}

function entryText(entry: Record<string, unknown>): string {
  return Object.entries(entry)
    .map(([k, v]) => `${k}: ${typeof v === 'string' ? v : JSON.stringify(v)}`)
    .join(' · ');
}

export function SolutionDialog({ onClose }: { onClose: () => void }) {
  const selectedSubProjectPath = useWorkspaceStore((s) => s.selectedSubProjectPath);
  const [tab, setTab] = useState<'export' | 'import'>('export');

  // Export state.
  const [nodePath, setNodePath] = useState(selectedSubProjectPath ?? DEFAULT_NODE_PATH);
  const [name, setName] = useState('');
  const [version, setVersion] = useState('1');
  const [seedTables, setSeedTables] = useState('');
  const [seedDomains, setSeedDomains] = useState('');
  const [exporting, setExporting] = useState(false);
  const [lastPackage, setLastPackage] = useState<SolutionPackage | null>(null);

  // Import state.
  const fileRef = useRef<HTMLInputElement>(null);
  const [importFile, setImportFile] = useState<{ name: string; pkg: SolutionPackage } | null>(null);
  const [targetNodePath, setTargetNodePath] = useState('');
  const [report, setReport] = useState<SolutionImportReport | null>(null);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleExport = async () => {
    if (exporting) return;
    setExporting(true);
    setError(null);
    try {
      const pkg = await SolutionsService.exportPackage({
        nodePath: nodePath.trim() || undefined,
        name: name.trim() || undefined,
        version: version.trim() || undefined,
        seedTables: seedTables.trim() || undefined,
        seedDomains: seedDomains.trim() || undefined,
      });
      setLastPackage(pkg);
      downloadPackage(pkg);
      showToast({ type: 'success', message: `Solution '${pkg.manifest.name}' exported (${pkg.flows.length} flows)` });
    } catch (e) {
      setError((e as Error).message); // e.g. unknown node path, missing workspace
    } finally {
      setExporting(false);
    }
  };

  const handleFilePicked = (file: File | undefined) => {
    if (!file) return;
    setError(null);
    setReport(null);
    file.text().then((text) => {
      try {
        const parsed = JSON.parse(text) as SolutionPackage;
        if (!parsed || typeof parsed !== 'object' || !parsed.manifest) throw new Error('missing manifest — not a solution package');
        setImportFile({ name: file.name, pkg: parsed });
      } catch (e) {
        setError(`Could not read '${file.name}': ${(e as Error).message}`);
        setImportFile(null);
      }
    });
  };

  const handleImport = async () => {
    if (!importFile || importing) return;
    setImporting(true);
    setError(null);
    try {
      const result = await SolutionsService.importPackage(importFile.pkg, targetNodePath.trim() || undefined);
      setReport(result);
      showToast({ type: 'success', message: `Solution '${importFile.pkg.manifest.name}' imported` });
    } catch (e) {
      setError((e as Error).message); // e.g. bad package shape, unknown target node
    } finally {
      setImporting(false);
    }
  };

  const inputCls = 'w-full px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 outline-none';
  const labelCls = 'text-[10px] font-medium text-gray-500 uppercase tracking-wider block mb-1';

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-3xl mx-4 shadow-2xl flex flex-col max-h-[85vh]"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700">
          <h3 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
            <Package className="w-4 h-4 text-indigo-400" />
            Solution Package
          </h3>
          <button className="text-gray-400 hover:text-gray-200 transition-colors" onClick={onClose} title="Close">✕</button>
        </div>

        {/* Tabs */}
        <div className="flex items-center gap-1 px-4 pt-3">
          {(['export', 'import'] as const).map((t) => (
            <button
              key={t}
              onClick={() => { setTab(t); setError(null); }}
              className={`px-3 py-1.5 rounded-md text-xs font-semibold capitalize transition-all ${
                tab === t ? 'bg-indigo-600 text-white shadow-sm' : 'text-gray-400 hover:text-gray-200 bg-gray-800/60'
              }`}
            >
              {t}
            </button>
          ))}
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {error && (
            <div className="flex items-center gap-2 px-3 py-2 rounded-lg bg-red-500/10 border border-red-500/30 text-xs text-red-400">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {error}
            </div>
          )}

          {tab === 'export' ? (
            <>
              <label className="block">
                <span className={labelCls}>Workspace Node</span>
                <input type="text" value={nodePath} onChange={(e) => setNodePath(e.target.value)} placeholder={DEFAULT_NODE_PATH} className={`${inputCls} font-mono`} />
              </label>
              <div className="grid grid-cols-2 gap-3">
                <label className="block">
                  <span className={labelCls}>Package Name (optional)</span>
                  <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="defaults to node name" className={inputCls} />
                </label>
                <label className="block">
                  <span className={labelCls}>Version</span>
                  <input type="text" value={version} onChange={(e) => setVersion(e.target.value)} className={`${inputCls} font-mono`} />
                </label>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <label className="block">
                  <span className={labelCls}>Seed Tables (comma list, optional)</span>
                  <input type="text" value={seedTables} onChange={(e) => setSeedTables(e.target.value)} placeholder="orders,customers" className={`${inputCls} font-mono`} />
                </label>
                <label className="block">
                  <span className={labelCls}>Seed Domains (comma list, optional)</span>
                  <input type="text" value={seedDomains} onChange={(e) => setSeedDomains(e.target.value)} placeholder="council-fees" className={`${inputCls} font-mono`} />
                </label>
              </div>
              <button className="btn btn-primary gap-1.5 text-xs disabled:opacity-50" onClick={handleExport} disabled={exporting}>
                {exporting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Download className="w-3.5 h-3.5" />}
                Export & Download
              </button>

              {/* Last export summary */}
              {lastPackage && (
                <div className="rounded-lg bg-gray-900/60 border border-gray-800 p-3 space-y-1">
                  <div className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">Last Export</div>
                  <div className="text-xs text-gray-200">
                    {lastPackage.manifest.name} v{lastPackage.manifest.version}{' '}
                    <span className="text-gray-500">from {lastPackage.manifest.sourceNodePath}</span>
                  </div>
                  <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-[10px] text-gray-400 font-mono">
                    <span>{lastPackage.flows.length} flows</span>
                    <span>{lastPackage.forms.length} forms</span>
                    <span>{lastPackage.attributeDomains.length} domains</span>
                    <span>{lastPackage.dynamicApis.length} APIs</span>
                    <span>{lastPackage.dataExchangeProfiles.length} DX profiles</span>
                    <span>{lastPackage.rules.length} rules</span>
                    <span>{lastPackage.eavEntities.length} EAV entities</span>
                    <span>{lastPackage.eavRows.length} EAV row sets</span>
                    <span>{lastPackage.dataSources.length} data sources</span>
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              {/* File picker */}
              <input ref={fileRef} type="file" accept=".json,application/json" className="hidden" onChange={(e) => handleFilePicked(e.target.files?.[0])} />
              {!importFile ? (
                <button className="btn gap-1.5 text-xs w-full justify-center py-6 border-dashed" onClick={() => fileRef.current?.click()}>
                  <Upload className="w-3.5 h-3.5" /> Choose a solution .json file
                </button>
              ) : (
                <div className="rounded-lg bg-gray-900/60 border border-gray-800 p-3 space-y-1">
                  <div className="text-xs text-gray-200 font-mono truncate" title={importFile.name}>{importFile.name}</div>
                  <div className="text-[11px] text-gray-400">
                    {importFile.pkg.manifest.name} v{importFile.pkg.manifest.version}{' '}
                    <span className="text-gray-600">(exported from {importFile.pkg.manifest.sourceNodePath})</span>
                  </div>
                  <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-[10px] text-gray-400 font-mono">
                    <span>{importFile.pkg.flows.length} flows</span>
                    <span>{importFile.pkg.forms.length} forms</span>
                    <span>{importFile.pkg.rules.length} rules</span>
                    <span>{importFile.pkg.eavEntities.length} EAV entities</span>
                  </div>
                </div>
              )}

              <label className="block">
                <span className={labelCls}>Target Node (optional — defaults to the package's source node)</span>
                <input type="text" value={targetNodePath} onChange={(e) => setTargetNodePath(e.target.value)} placeholder={DEFAULT_NODE_PATH} className={`${inputCls} font-mono`} />
              </label>

              <button className="btn btn-primary gap-1.5 text-xs disabled:opacity-50" onClick={handleImport} disabled={!importFile || importing}>
                {importing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
                Import Solution
              </button>

              {/* Import report */}
              {report && (
                <div className="rounded-lg bg-gray-900/60 border border-gray-800 overflow-hidden">
                  <div className="px-3 py-1.5 border-b border-gray-800/60">
                    <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider">Import Report</span>
                  </div>
                  <div className="p-3 space-y-2 max-h-64 overflow-y-auto">
                    {REPORT_SECTIONS.map(({ key, label }) => {
                      const entries = report[key];
                      if (!entries || entries.length === 0) return null;
                      return (
                        <div key={key}>
                          <div className="text-[10px] font-semibold text-gray-400">{label} ({entries.length})</div>
                          {entries.map((entry, i) => (
                            <div key={i} className="text-[10px] font-mono text-gray-500 truncate pl-2" title={entryText(entry)}>
                              {entryText(entry)}
                            </div>
                          ))}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
