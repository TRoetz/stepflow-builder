import { X } from 'lucide-react';

interface HelpModalProps {
  onClose: () => void;
}

// Mirrors the bindings registered by useKeyboardShortcuts (single registration, App-level).
const SHORTCUTS: Array<[string, string]> = [
  ['Ctrl + Z', 'Undo'],
  ['Ctrl + Y / Ctrl + Shift + Z', 'Redo'],
  ['Ctrl + S', 'Save (to selected Workspace sub-project, else this browser)'],
  ['Ctrl + Enter', 'Run / Stop execution'],
  ['Ctrl + Shift + F', 'Auto-arrange nodes'],
  ['Ctrl + A', 'Select all nodes'],
  ['Ctrl + C / Ctrl + V', 'Copy / paste selected nodes'],
  ['Ctrl + D', 'Duplicate selected nodes'],
  ['Delete / Backspace', 'Delete selected node(s)'],
  ['Escape', 'Deselect all'],
];

/** In-app keyboard-shortcut and persistence reference. */
export function HelpModal({ onClose }: HelpModalProps) {
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-lg mx-4 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700">
          <h3 className="text-sm font-semibold text-gray-100">Help — Keyboard Shortcuts</h3>
          <button className="text-gray-400 hover:text-gray-200 transition-colors" onClick={onClose} title="Close">
            <X className="w-4 h-4" />
          </button>
        </div>
        <div className="px-4 py-3 max-h-[70vh] overflow-y-auto">
          <table className="w-full text-sm">
            <tbody>
              {SHORTCUTS.map(([key, action]) => (
                <tr key={key} className="border-b border-gray-800 last:border-0">
                  <td className="py-1.5 pr-4 whitespace-nowrap">
                    <kbd className="px-1.5 py-0.5 rounded bg-gray-800 border border-gray-700 text-xs text-gray-200 font-mono">{key}</kbd>
                  </td>
                  <td className="py-1.5 text-gray-300">{action}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="mt-3 text-xs text-gray-500 leading-relaxed">
            Save stores to the selected Workspace sub-project when one is active (server-side, shared across
            machines); otherwise it stores in this browser only. The Load dialog lists both sources with a badge.
            Run uses Simulation or Live Backend mode — choose it with the toolbar mode toggle.
          </p>
        </div>
        <div className="flex justify-end px-4 py-3 border-t border-gray-700">
          <button
            className="px-3 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 transition-colors"
            onClick={onClose}
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
