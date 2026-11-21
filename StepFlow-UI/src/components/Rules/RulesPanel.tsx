import { useCallback, useEffect, useState } from 'react';
import Editor from '@monaco-editor/react';
import { Scale, X, Plus, Trash2, AlertCircle, Loader2 } from 'lucide-react';
import { RulesService, RULE_KINDS, type NamedRule } from '@services/rulesService';
import { showToast } from '@stores/useToastStore';

// ═══════════════════════════════════════════════════════════
// Rules Panel — manage named rules (choice / jsonata / sql /
// ms-rules / ai-decision). Left: rule list + New. Right: name,
// kind, description and a Monaco JSON definition editor with
// per-kind starter templates. Save upserts; Delete confirms first.
// ═══════════════════════════════════════════════════════════

const KIND_TEMPLATES: Record<string, string> = {
  choice: '{\n  "conditions": [\n    {\n      "name": "branch-a",\n      "expression": "{{input.amount > 100}}"\n    }\n  ],\n  "default": "none"\n}',
  jsonata: '{\n  "expression": "$string(input.name)",\n  "outputKey": "result"\n}',
  sql: '{\n  "expression": "{{amount}} > 100",\n  "requiredParameters": ["amount"]\n}',
  'ms-rules': '{\n  "rules": [\n    {\n      "ruleName": "first-rule",\n      "expression": "input.amount > 100"\n    }\n  ]\n}',
  'ai-decision': '{\n  "prompt": "Decide whether {{input.text}} should be approved.",\n  "options": ["approve", "reject"]\n}',
};

function kindBadge(kind: string): string {
  switch (kind) {
    case 'choice': return 'bg-indigo-500/15 text-indigo-300';
    case 'jsonata': return 'bg-cyan-500/15 text-cyan-300';
    case 'sql': return 'bg-emerald-500/15 text-emerald-300';
    case 'ms-rules': return 'bg-purple-500/15 text-purple-300';
    case 'ai-decision': return 'bg-pink-500/15 text-pink-300';
    default: return 'bg-gray-700/60 text-gray-400';
  }
}

export function RulesPanel({ onClose }: { onClose: () => void }) {
  const [rules, setRules] = useState<NamedRule[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  // Editor state (draft until Save).
  const [name, setName] = useState('');
  const [kind, setKind] = useState<string>('choice');
  const [description, setDescription] = useState('');
  const [definitionText, setDefinitionText] = useState(KIND_TEMPLATES.choice);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadList = useCallback(async (select?: string) => {
    try {
      const list = await RulesService.list();
      setRules(list);
      setLoadError(null);
      if (select !== undefined) applyRule(select, list);
    } catch (e) {
      setLoadError((e as Error).message);
    }
  }, []);

  const applyRule = useCallback((ruleName: string | null, list?: NamedRule[]) => {
    setSelectedName(ruleName);
    if (!ruleName) return;
    const rule = (list ?? []).find((r) => r.name === ruleName);
    if (!rule) return; // the polling pass will fill it in via get() below
    setName(rule.name);
    setKind(rule.kind);
    setDescription(rule.description ?? '');
    setDefinitionText(JSON.stringify(rule.definition, null, 2));
  }, []);

  useEffect(() => { void loadList(); }, [loadList]);

  // Load the full rule (definition) when the selection changes.
  useEffect(() => {
    if (!selectedName) return;
    let cancelled = false;
    RulesService.get(selectedName)
      .then((rule) => {
        if (cancelled) return;
        setName(rule.name);
        setKind(rule.kind);
        setDescription(rule.description ?? '');
        setDefinitionText(JSON.stringify(rule.definition, null, 2));
      })
      .catch(() => { /* rule may have been deleted elsewhere */ });
    return () => { cancelled = true; };
  }, [selectedName]);

  const startNewRule = () => {
    setSelectedName(null);
    setName('');
    setKind('choice');
    setDescription('');
    setDefinitionText(KIND_TEMPLATES.choice ?? '{}');
  };

  const changeKind = (k: string) => {
    setKind(k);
    // Only replace the definition when it is still the previous kind's untouched template.
    if (Object.values(KIND_TEMPLATES).some((t) => t === definitionText)) {
      setDefinitionText(KIND_TEMPLATES[k] ?? '{}');
    }
  };

  const handleSave = async () => {
    if (!name.trim() || saving) return;
    let definition: unknown;
    try {
      definition = JSON.parse(definitionText || '{}');
    } catch (e) {
      showToast({ type: 'error', message: `Invalid definition JSON — ${(e as Error).message}` });
      return;
    }
    setSaving(true);
    try {
      const saved = await RulesService.save({ name: name.trim(), kind, description: description || null, definition });
      showToast({ type: 'success', message: `Rule '${saved.name}' saved` });
      await loadList(saved.name);
    } catch (e) {
      showToast({ type: 'error', message: (e as Error).message }); // surfaces engine validation errors (sql/ms-rules)
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!selectedName || saving) return;
    if (!window.confirm(`Delete rule '${selectedName}'? Flows referencing it will fail to resolve.`)) return;
    setSaving(true);
    try {
      await RulesService.remove(selectedName);
      showToast({ type: 'success', message: `Rule '${selectedName}' deleted` });
      startNewRule();
      await loadList();
    } catch (e) {
      showToast({ type: 'error', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="w-[880px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <Scale className="w-4 h-4 text-indigo-400" />
          Rules
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title="Close Rules Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body: two columns */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left column: rule list + New */}
        <div className="w-[240px] shrink-0 border-r border-gray-800 flex flex-col min-h-0">
          {loadError && (
            <div className="flex items-center gap-2 px-3 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
              <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {loadError}
            </div>
          )}
          <div className="px-3 py-2 border-b border-gray-800">
            <button className="btn btn-primary gap-1.5 text-xs w-full justify-center" onClick={startNewRule}>
              <Plus className="w-3.5 h-3.5" /> New Rule
            </button>
          </div>
          <div className="flex-1 overflow-y-auto">
            {rules.length === 0 ? (
              <div className="text-xs text-gray-600 italic px-4 py-6 text-center">No named rules yet.</div>
            ) : (
              rules.map((r) => {
                const selected = r.name === selectedName;
                return (
                  <button
                    key={r.name}
                    onClick={() => applyRule(r.name, rules)}
                    className={`w-full text-left px-3 py-2.5 border-b border-gray-800/60 transition-colors ${selected ? 'bg-indigo-600/15' : 'hover:bg-gray-800/40'}`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`text-xs truncate flex-1 ${selected ? 'text-gray-100' : 'text-gray-300'}`}>{r.name}</span>
                      <span className={`shrink-0 px-1.5 py-0.5 rounded text-[8px] font-bold ${kindBadge(r.kind)}`}>
                        {String(r.kind).toUpperCase()}
                      </span>
                    </div>
                  </button>
                );
              })
            )}
          </div>
        </div>

        {/* Right column: editor */}
        <div className="flex-1 overflow-y-auto p-4 min-h-0">
          <div className="space-y-3 max-w-[560px]">
            {/* Name + kind */}
            <div className="grid grid-cols-[1fr_150px] gap-2">
              <label className="block">
                <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block mb-1">Name</span>
                <input
                  type="text"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="my-rule-name"
                  className="w-full px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 font-mono outline-none"
                />
              </label>
              <label className="block">
                <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block mb-1">Kind</span>
                <select
                  value={kind}
                  onChange={(e) => changeKind(e.target.value)}
                  className="w-full px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 outline-none"
                >
                  {RULE_KINDS.map((k) => (
                    <option key={k} value={k}>{k}</option>
                  ))}
                </select>
              </label>
            </div>

            {/* Description */}
            <label className="block">
              <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block mb-1">Description</span>
              <input
                type="text"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="What this rule decides"
                className="w-full px-2.5 py-1.5 rounded-lg bg-gray-800 border border-gray-700 focus:border-indigo-500 text-xs text-gray-200 outline-none"
              />
            </label>

            {/* Definition editor */}
            <div className="space-y-1">
              <span className="text-[10px] font-medium text-gray-500 uppercase tracking-wider block">Definition (JSON)</span>
              <Editor
                height={320}
                language="json"
                theme="vs-dark"
                value={definitionText}
                onChange={(v) => setDefinitionText(v ?? '{}')}
                options={{ minimap: { enabled: false }, fontSize: 11, scrollBeyondLastLine: false }}
              />
            </div>

            {/* Actions */}
            <div className="flex items-center gap-2">
              <button
                className="btn btn-primary gap-1.5 text-xs disabled:opacity-50"
                onClick={handleSave}
                disabled={!name.trim() || saving}
              >
                {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null}
                Save Rule
              </button>
              {selectedName && (
                <button className="btn gap-1.5 text-xs disabled:opacity-50" onClick={handleDelete} disabled={saving}>
                  <Trash2 className="w-3.5 h-3.5 text-red-400" /> Delete
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
