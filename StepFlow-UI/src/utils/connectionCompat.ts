// ═══════════════════════════════════════════════════════════
// Connection Compatibility Utilities (P0: "Add next step")
// ═══════════════════════════════════════════════════════════

import { DataType, NodeData, StepInput, StepOutput } from '@schema-types/schema';
import { schemaById } from '@schemas/index';

/**
 * Two port types are compatible when they match exactly, or when either side
 * is `any` (wildcard). This mirrors the visual affordance of type-colored
 * handles and keeps v1 behavior predictable.
 */
export function areTypesCompatible(a: DataType, b: DataType): boolean {
  return a === 'any' || b === 'any' || a === b;
}

/**
 * Resolve a node's port definition for a handle id (falls back to the first port,
 * matching React Flow's default-handle behavior for collapsed nodes).
 */
export function resolvePortType(
  nodeData: NodeData | undefined,
  side: 'input' | 'output',
  handleId?: string
): StepInput | StepOutput | null {
  if (!nodeData?.schemaId) return null;
  const schema = schemaById.get(nodeData.schemaId);
  const ports = (side === 'input' ? schema?.inputs : schema?.outputs) ?? [];
  if (ports.length === 0) return null;
  return ports.find((p) => p.id === handleId) ?? ports[0];
}

/**
 * Type-compatibility check for a concrete port pair. Returns { ok: true } when
 * the types are compatible or unknown (no schema / no typed ports), so only
 * genuinely mismatched pairs are blocked — consistent with findFirstCompatiblePair.
 */
export function checkPortCompatibility(
  sourceHandleId: string | null | undefined,
  targetHandleId: string | null | undefined,
  sourceData: NodeData | undefined,
  targetData: NodeData | undefined
): { ok: boolean; reason?: string } {
  const src = resolvePortType(sourceData, 'output', sourceHandleId ?? undefined);
  const tgt = resolvePortType(targetData, 'input', targetHandleId ?? undefined);
  if (!src || !tgt) return { ok: true };

  const compatible = areTypesCompatible(src.type, tgt.type);
  if (compatible) return { ok: true };
  return {
    ok: false,
    reason: `Incompatible types: ${src.label} (${src.type}) → ${tgt.label} (${tgt.type})`,
  };
}

/**
 * Find the first (output, input) port pair between two port sets that is
 * type-compatible. Iterates outputs in declaration order, so the "primary"
 * output usually wins — which matches how users think about "the next step".
 */
export function findFirstCompatiblePair(
  outputs: StepOutput[],
  inputs: StepInput[]
): { sourceHandle?: string; targetHandle?: string } | null {
  for (const out of outputs) {
    const inp = inputs.find((i) => areTypesCompatible(out.type, i.type));
    if (inp) {
      return { sourceHandle: out.id, targetHandle: inp.id };
    }
  }
  return null;
}

/**
 * Whether a candidate step can be connected downstream of the given outputs.
 * A node with no outputs (e.g. End) or a candidate with no inputs is never connectable.
 */
export function isConnectable(outputs: StepOutput[], inputs: StepInput[]): boolean {
  if (outputs.length === 0 || inputs.length === 0) return false;
  return findFirstCompatiblePair(outputs, inputs) !== null;
}

/**
 * Label of the first compatible input port on a candidate step — used in the
 * "Add next" popover so users see which port receives the connection.
 */
export function firstCompatibleInputLabel(
  outputs: StepOutput[],
  inputs: StepInput[]
): string | null {
  for (const out of outputs) {
    const inp = inputs.find((i) => areTypesCompatible(out.type, i.type));
    if (inp) return inp.label;
  }
  return null;
}
