// ============================================================================
// AiBuildModal — generate a new Data Exchange profile (or refine the selected
// one) from a natural-language description + optional sample data, using the
// saved local model config. The result lands in the editor as an unsaved draft.
// ============================================================================

import { useState } from 'react';
import { Loader2, Sparkles, X } from 'lucide-react';
import { buildProfileWithAi } from '@services/aiProfileBuilder';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import type { DataExchangeProfile } from '@services/dataExchangeService';

interface AiBuildModalProps {
  open: boolean;
  /** Non-null → refine mode (current profile offered as base). */
  baseProfile: DataExchangeProfile | null;
  onClose: () => void;
  onGenerated: (jsonText: string) => void;
}

export function AiBuildModal({ open, baseProfile, onClose, onGenerated }: AiBuildModalProps) {
  const [description, setDescription] = useState('');
  const [sampleData, setSampleData] = useState('');
  const [useBase, setUseBase] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const savedModel = useAiModelConfigStore((s) => s.defaultModel);
  const hasConfig = useAiModelConfigStore((s) => !!s.baseUrl && !!s.defaultModel);

  if (!open) return null;

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const profile = await buildProfileWithAi({
        description,
        sampleData: sampleData || undefined,
        baseProfile: useBase ? baseProfile : null,
      });
      onGenerated(JSON.stringify(profile, null, 2));
      setDescription('');
      setSampleData('');
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  const canSubmit = hasConfig && description.trim().length > 0 && !busy;

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/70 p-4" onClick={onClose}>
      <div
        className="w-full max-w-lg rounded-xl border border-gray-800 bg-gray-900 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center gap-2 px-4 py-3 border-b border-gray-800">
          <Sparkles className="w-4 h-4 text-indigo-400" />
          <span className="text-sm font-semibold text-gray-200">
            {baseProfile ? 'Refine Profile with AI' : 'AI Build Profile'}
          </span>
          <button onClick={onClose} title="Close" className="ml-auto p-1 rounded text-gray-500 hover:text-gray-200 transition-colors">
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Body */}
        <div className="p-4 space-y-3">
          <p className={`text-[11px] ${hasConfig ? 'text-gray-500' : 'text-amber-400'}`}>
            {hasConfig
              ? `Model: ${savedModel} (from Settings → AI Model)`
              : 'No AI model configured — open Settings → AI Model and save a local endpoint first.'}
          </p>

          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider font-semibold text-gray-500" htmlFor="ai-description">
              What should this profile do?
            </label>
            <textarea
              id="ai-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder={'e.g. Import customer orders CSV, map OrderNumber→order_id and CustomerId→customer_id, enrich with product prices from the products API, dispatch to file://out/orders.csv'}
              className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg p-2 text-xs text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500/50 transition-colors"
            />
          </div>

          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider font-semibold text-gray-500" htmlFor="ai-sample">
              Sample data (optional — CSV or JSON)
            </label>
            <textarea
              id="ai-sample"
              value={sampleData}
              onChange={(e) => setSampleData(e.target.value)}
              rows={4}
              placeholder={'OrderNumber,CustomerId,Quantity\nA-1,9,3'}
              className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg p-2 font-mono text-[11px] text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500/50 transition-colors"
            />
          </div>

          {baseProfile && (
            <label className="flex items-center gap-2 text-xs text-gray-300 cursor-pointer">
              <input type="checkbox" checked={useBase} onChange={(e) => setUseBase(e.target.checked)} />
              Use current profile as base (modify instead of rebuild)
            </label>
          )}

          {error && (
            <div className="rounded-lg border border-red-500/40 bg-red-500/10 px-3 py-2 text-[11px] text-red-300 whitespace-pre-wrap break-all">
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 px-4 py-3 border-t border-gray-800">
          <button
            onClick={onClose}
            className="px-3 py-1.5 rounded-lg border border-gray-700/60 text-xs font-semibold text-gray-400 hover:text-gray-200 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={() => void submit()}
            disabled={!canSubmit}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 text-white text-xs font-semibold transition-colors"
          >
            {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Sparkles className="w-3.5 h-3.5" />}
            {busy ? 'Generating…' : baseProfile ? 'Refine Profile' : 'Generate Profile'}
          </button>
        </div>
      </div>
    </div>
  );
}
