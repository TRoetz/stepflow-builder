// ============================================================================
// AiFormBuildModal — generate a form definition, attribute domain contract, and
// capture/save flow from a natural-language description + optional sample data,
// using the saved local model config. The result lands in the forms editor as an
// unsaved draft (plus a banner offering to open the generated flow on canvas).
// ============================================================================

import { useState } from 'react';
import { Loader2, Sparkles, X } from 'lucide-react';
import { buildFormWithAi, type AiFormPlan } from '@services/aiFormBuilder';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';

interface AiFormBuildModalProps {
  open: boolean;
  onClose: () => void;
  onGenerated: (plan: AiFormPlan) => void;
}

export function AiFormBuildModal({ open, onClose, onGenerated }: AiFormBuildModalProps) {
  const [description, setDescription] = useState('');
  const [sampleData, setSampleData] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const savedModel = useAiModelConfigStore((s) => s.defaultModel);
  const hasConfig = useAiModelConfigStore((s) => !!s.baseUrl && !!s.defaultModel);

  if (!open) return null;

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const plan = await buildFormWithAi({
        description,
        sampleData: sampleData || undefined,
      });
      onGenerated(plan);
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
          <span className="text-sm font-semibold text-gray-200">AI Build Form &amp; Flow</span>
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
            <label className="text-[10px] uppercase tracking-wider font-semibold text-gray-500" htmlFor="ai-form-description">
              What should this form capture, and where should the data go?
            </label>
            <textarea
              id="ai-form-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder={'e.g. Capture customer orders (order number, customer name, line items with quantity and price, ship date). Save each submission to the OrderApproval store.'}
              className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg p-2 text-xs text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500/50 transition-colors"
            />
          </div>

          <div className="space-y-1">
            <label className="text-[10px] uppercase tracking-wider font-semibold text-gray-500" htmlFor="ai-form-sample">
              Sample data (optional — CSV or JSON)
            </label>
            <textarea
              id="ai-form-sample"
              value={sampleData}
              onChange={(e) => setSampleData(e.target.value)}
              rows={4}
              placeholder={'OrderNumber,CustomerName,Quantity\nA-1,Alice,3'}
              className="w-full bg-gray-800/50 border border-gray-700/50 rounded-lg p-2 font-mono text-[11px] text-gray-200 placeholder-gray-600 focus:outline-none focus:border-indigo-500/50 transition-colors"
            />
          </div>

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
            {busy ? 'Generating…' : 'Generate Form & Flow'}
          </button>
        </div>
      </div>
    </div>
  );
}
