// ═══════════════════════════════════════════════════════════
// Variable picker UI — insert {{node.field}} tokens into config
// textareas, plus the EAV row-column chip picker.
// ═══════════════════════════════════════════════════════════

import { useEffect, useMemo, useRef, useState } from 'react';
import { Braces, Plus, X } from 'lucide-react';
import type { ConfigField } from '@schemas/index';
import { useInScopeVariables, useSubflowRowFields } from '@hooks/useInScopeVariables';
import { parseColumns } from '@utils/variables';

// ── Textarea with variable insertion (used for all textarea config fields) ──

interface TextAreaWithVariablesProps {
  field: ConfigField;
  value: unknown;
  /** Shown when the stored value is still empty (mirrors the plain renderer). */
  defaultValue?: unknown;
  contextNodeId: string;
  onChange: (value: unknown) => void;
}

export function TextAreaWithVariables({
  field,
  value,
  defaultValue,
  contextNodeId,
  onChange,
}: TextAreaWithVariablesProps) {
  const displayValue =
    typeof value === 'string'
      ? value
      : typeof defaultValue === 'string'
        ? defaultValue
        : '';
  const [open, setOpen] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const variables = useInScopeVariables(contextNodeId);

  // Close on outside click
  useEffect(() => {
    if (!open) return;
    const handler = (e: PointerEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', handler);
    return () => document.removeEventListener('pointerdown', handler);
  }, [open]);

  const grouped = useMemo(() => {
    const map = new Map<string, typeof variables>();
    for (const v of variables) {
      const list = map.get(v.nodeLabel);
      if (list) list.push(v);
      else map.set(v.nodeLabel, [v]);
    }
    return Array.from(map.entries());
  }, [variables]);

  const insertToken = (token: string) => {
    const el = textareaRef.current;
    const text = displayValue;
    let next: string;

    if (el && document.activeElement === el) {
      const start = el.selectionStart ?? text.length;
      const end = el.selectionEnd ?? start;
      next = `${text.slice(0, start)}{{${token}}}${text.slice(end)}`;
      requestAnimationFrame(() => {
        if (!el) return;
        el.focus();
        const pos = start + token.length + 4;
        el.setSelectionRange(pos, pos);
      });
    } else {
      next = text && !/\s$/.test(text) ? `${text} {{${token}}}` : `${text}{{${token}}}`.trimStart();
    }

    onChange(next);
    setOpen(false);
    textareaRef.current?.focus();
  };

  return (
    <div ref={wrapperRef} className="relative">
      <label className="text-xs text-gray-400 block mb-1 flex items-center justify-between">
        <span>
          {field.label}
          {field.required && <span className="text-red-400 ml-1">*</span>}
        </span>
        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          disabled={variables.length === 0}
          title={
            variables.length > 0
              ? 'Insert variable from upstream nodes'
              : 'No upstream variables available — connect this node to a data source first'
          }
          className={`flex items-center gap-1 text-[10px] font-medium px-1.5 py-0.5 rounded transition-colors ${
            open
              ? 'bg-indigo-500/20 text-indigo-300'
              : variables.length > 0
                ? 'text-gray-400 hover:text-indigo-300 hover:bg-gray-800'
                : 'text-gray-600 cursor-not-allowed'
          }`}
        >
          <Braces className="w-3 h-3" />
          Variables ({variables.length})
        </button>
      </label>

      <textarea
        ref={textareaRef}
        value={displayValue}
        onChange={(e) => onChange(e.target.value)}
        rows={Math.max(3, Math.min(8, displayValue.split('\n').length))}
        placeholder={field.description}
        className="w-full px-3 py-1.5 text-sm rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent resize-y font-mono"
      />

      {open && (
        <div className="absolute left-0 right-0 top-full mt-1 z-30 max-h-64 overflow-y-auto rounded-lg border border-gray-700 bg-gray-900 shadow-xl shadow-black/50">
          {variables.length === 0 ? (
            <div className="px-3 py-2 text-xs text-gray-500 italic">
              No upstream variables in scope.
            </div>
          ) : (
            grouped.map(([label, vars]) => (
              <div key={label}>
                <div className="px-3 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-wider text-gray-500 sticky top-0 bg-gray-900">
                  {label}
                </div>
                {vars.map((v) => (
                  <button
                    key={v.token}
                    type="button"
                    onClick={() => insertToken(v.token)}
                    className="w-full text-left px-3 py-1.5 hover:bg-indigo-500/10 transition-colors flex items-center gap-2"
                    title={v.description}
                  >
                    <code className="text-xs text-emerald-400 bg-emerald-400/10 rounded px-1.5 py-0.5">
                      {'{{' + v.token + '}}'}
                    </code>
                    {v.fieldPath.length > 0 && (
                      <span className="text-[10px] text-gray-500 truncate">
                        {v.description ?? v.type}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}

// ── EAV row-column picker (chips from the Map loop's data source) ──

interface EavColumnPickerProps {
  value: unknown;
  onChange: (value: string) => void;
}

export function EavColumnPicker({ value, onChange }: EavColumnPickerProps) {
  const rowScope = useSubflowRowFields();
  const [manual, setManual] = useState('');
  const currentColumns = useMemo(() => parseColumns(value), [value]);

  // Candidate columns: discovered loop-scope columns + any already selected.
  const candidates = useMemo(() => {
    const seen = new Set<string>();
    const out: Array<{ name: string; fromScope: boolean }> = [];
    for (const f of rowScope?.fields ?? []) {
      if (!seen.has(f.name.toLowerCase())) {
        seen.add(f.name.toLowerCase());
        out.push({ name: f.name, fromScope: true });
      }
    }
    for (const c of currentColumns) {
      if (!seen.has(c.toLowerCase())) {
        seen.add(c.toLowerCase());
        out.push({ name: c, fromScope: false });
      }
    }
    return out;
  }, [rowScope, currentColumns]);

  const toggle = (name: string) => {
    const set = new Set(currentColumns.map((c) => c));
    if (set.has(name)) set.delete(name);
    else set.add(name);
    onChange(Array.from(set).join(', '));
  };

  const addManual = () => {
    const name = manual.trim();
    if (!name || currentColumns.some((c) => c.toLowerCase() === name.toLowerCase())) return;
    onChange([...currentColumns, name].join(', '));
    setManual('');
  };

  return (
    <div>
      <div className="text-xs font-medium text-gray-400 uppercase tracking-wider mb-2">
        Row Columns
      </div>

      {rowScope ? (
        <p className="text-[11px] text-gray-500 mb-2 leading-relaxed">
          Loop scope discovered: the parent flow's Map state{' '}
          <span className="text-indigo-400 font-medium">"{rowScope.mapNodeLabel}"</span> iterates over{' '}
          <span className="text-emerald-400 font-medium">{rowScope.producerLabel}</span>. Pick the columns each row exposes:
        </p>
      ) : (
        <p className="text-[11px] text-gray-500 mb-2 leading-relaxed">
          No Map loop with declared output columns found above this node. Add column names manually — they must match
          keys in each row object.
        </p>
      )}

      {candidates.length > 0 && (
        <div className="flex flex-wrap gap-1.5 mb-2">
          {candidates.map((c) => {
            const active = currentColumns.some((x) => x.toLowerCase() === c.name.toLowerCase());
            return (
              <button
                key={c.name}
                type="button"
                onClick={() => toggle(c.name)}
                className={`text-[11px] font-mono px-2 py-0.5 rounded-full border transition-colors ${
                  active
                    ? 'bg-indigo-500/20 border-indigo-400/60 text-indigo-300'
                    : 'border-gray-700 bg-gray-800/50 text-gray-400 hover:border-gray-500'
                }`}
              >
                {c.name}
              </button>
            );
          })}
        </div>
      )}

      <div className="flex items-center gap-1.5">
        <input
          type="text"
          value={manual}
          onChange={(e) => setManual(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              addManual();
            }
          }}
          placeholder="Add a column name…"
          className="flex-1 px-2.5 py-1 text-xs rounded-lg bg-gray-800 border border-gray-700 text-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        />
        <button
          type="button"
          onClick={addManual}
          disabled={!manual.trim()}
          className="p-1.5 rounded-lg bg-gray-800 border border-gray-700 text-gray-400 hover:text-indigo-300 disabled:opacity-40 transition-colors"
        >
          <Plus className="w-3.5 h-3.5" />
        </button>
      </div>

      {currentColumns.length > 0 && (
        <div className="mt-2 flex items-start gap-1.5 text-[10px] text-gray-500">
          <span className="shrink-0">Selected:</span>
          <code className="text-emerald-400/80 break-all">{currentColumns.join(', ')}</code>
        </div>
      )}

      {value !== undefined && value !== '' && (
        <button
          type="button"
          onClick={() => onChange('')}
          className="mt-2 flex items-center gap-1 text-[10px] text-gray-500 hover:text-red-400 transition-colors"
        >
          <X className="w-3 h-3" /> Clear columns
        </button>
      )}
    </div>
  );
}
