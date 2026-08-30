import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';
import { stepIcon } from '@components/Nodes/stepIcons';
import { useNodeStore } from '@stores/useNodeStore';
import { schemaById, stepSchemas } from '@schemas/index';
import { categoryById } from '@schemas/categories';
import { firstCompatibleInputLabel, isConnectable } from '@utils/connectionCompat';

/**
 * "Add next step" popover (P0).
 *
 * Renders via portal with `position: fixed` anchored to the + chip that opened
 * it. Shows only steps whose input ports are type-compatible with the source
 * node's outputs, grouped/sorted by palette category order.
 */

const POP_W = 264; // px — used for viewport clamping
const POP_MAX_H = 352; // px — worst-case height estimate (header + max-h list)

// Palette category display order (insertion order of the registry).
const CATEGORY_ORDER = new Map<string, number>(
  [...categoryById.keys()].map((key, i) => [String(key), i])
);

interface AddNextPopoverProps {
  sourceNodeId: string;
  /** Viewport coordinates of the chip's bottom-right corner */
  anchor: { x: number; y: number };
  onPick: (schemaId: string) => void;
  onClose: () => void;
}

export function AddNextPopover({ sourceNodeId, anchor, onPick, onClose }: AddNextPopoverProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [measuredH, setMeasuredH] = useState<number | null>(null);
  const node = useNodeStore((s) => s.nodes.find((n) => n.id === sourceNodeId));
  const schema = node ? schemaById.get(node.data.schemaId as string) : undefined;

  // Close if the source node is removed while the popover is open.
  useEffect(() => {
    if (!node) onClose();
  }, [node, onClose]);

  // Close on outside click / Escape / wheel (canvas pan or zoom would desync the anchor).
  useEffect(() => {
    const onPointerDown = (e: PointerEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose();
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    const onWheel = () => onClose();
    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('keydown', onKeyDown);
    window.addEventListener('wheel', onWheel, { passive: true });
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true);
      document.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('wheel', onWheel);
    };
  }, [onClose]);

  const candidates = useMemo(() => {
    if (!schema || schema.outputs.length === 0) return [];
    return stepSchemas
      .filter((s) => !s.deprecated && isConnectable(schema.outputs, s.inputs))
      .sort(
        (a, b) =>
          (CATEGORY_ORDER.get(String(a.category)) ?? 99) -
            (CATEGORY_ORDER.get(String(b.category)) ?? 99) || a.name.localeCompare(b.name)
      );
  }, [schema]);

  // Measure the rendered height so we can flip above the anchor when there is
  // no room below (e.g. anchors near the status bar at the bottom of screen).
  useLayoutEffect(() => {
    if (ref.current) setMeasuredH(ref.current.offsetHeight);
  }, [candidates.length]);

  if (!node || !schema) return null;

  const vw = window.innerWidth;
  const vh = window.innerHeight;
  // Popover sits under the node's right side: its right edge aligns near the chip.
  const left = Math.max(8, Math.min(anchor.x - POP_W + 40, vw - POP_W - 8));
  const estH = measuredH ?? POP_MAX_H;
  const openUp = anchor.y + POP_MAX_H > vh - 16;
  const top = openUp
    ? Math.max(8, anchor.y - estH - 6)
    : Math.max(8, Math.min(anchor.y + 6, vh - estH));

  return createPortal(
    <div
      ref={ref}
      role="menu"
      aria-label={`Add next step after ${node.data.label || 'step'}`}
      style={{ position: 'fixed', left, top, width: POP_W, opacity: measuredH === null ? 0 : 1 }}
      className="z-[999] rounded-lg border border-slate-700 bg-slate-900/95 backdrop-blur-sm shadow-2xl overflow-hidden"
    >
      <div className="flex items-center justify-between gap-2 px-3 py-2 border-b border-slate-800">
        <span className="text-xs font-medium text-gray-300 truncate">
          After{' '}
          <span className="text-indigo-400 truncate">{node.data.label || schema.name}</span>
        </span>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close"
          className="p-0.5 rounded text-gray-500 hover:text-gray-300 hover:bg-white/10 shrink-0"
        >
          <X className="w-3.5 h-3.5" />
        </button>
      </div>

      {candidates.length === 0 ? (
        <div className="px-3 py-4 text-xs text-gray-500">
          No steps accept this node's output type(s).
        </div>
      ) : (
        <div className="max-h-[296px] overflow-y-auto overscroll-contain py-1">
          {candidates.map((s) => {
            const portLabel = firstCompatibleInputLabel(schema.outputs, s.inputs);
            return (
              <button
                key={s.schemaId}
                type="button"
                role="menuitem"
                onClick={() => onPick(s.schemaId)}
                className="w-full flex items-center gap-2.5 px-3 py-1.5 text-left hover:bg-indigo-500/10 focus:bg-indigo-500/10 transition-colors outline-none"
              >
                <span className="w-6 h-6 rounded-md bg-white/5 flex items-center justify-center shrink-0 overflow-hidden">
                  {stepIcon(s.icon, 'w-3.5 h-3.5')}
                </span>
                <span className="flex-1 min-w-0">
                  <span className="block text-xs font-medium text-gray-200 truncate">{s.name}</span>
                  {portLabel && (
                    <span className="block text-[10px] text-gray-500 truncate">
                      → connects to “{portLabel}”
                    </span>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>,
    document.body
  );
}
