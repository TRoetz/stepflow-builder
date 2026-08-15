// ═══════════════════════════════════════════════════════════
// Variable Engine — {{node.field}} tokens for cross-node data flow
//
// - collectInScopeVariables: walks upstream edges from a node and
//   exposes every variable that can be referenced in its config.
// - interpolateVariables: replaces {{token}} placeholders at execution time.
// - findSubflowRowFields: resolves loop-scope row columns for EAV nodes by
//   tracing the parent flow's Map state back to its data source.
// ═══════════════════════════════════════════════════════════

import { schemaById } from '@schemas/index';
import type { StepSchema } from '@schemas/index';
import type { DataType } from '@schema-types/schema';

/** sessionStorage key holding the id of the flow currently open on the canvas. */
export const ACTIVE_FLOW_STORAGE_KEY = 'stepflow-active-flow-id';

const SAVED_FLOWS_STORAGE_KEY = 'stepflow-flows';
const TOKEN_RE = /\{\{([^{}]+)\}\}/g;

// ── Minimal structural view of a canvas node (store nodes satisfy this) ──
export interface VariableCanvasNode {
  id: string;
  type?: string;
  data?: {
    schemaId?: string;
    label?: string;
    configuration?: Record<string, unknown>;
  };
}

// ── A variable the user can insert into a config field ──
export interface InScopeVariable {
  /** Token without braces, e.g. "my_sql.name" */
  token: string;
  /** Human label of the producing node (used as group header) */
  nodeLabel: string;
  nodeId: string;
  /** [] means "entire node output" */
  fieldPath: string[];
  type: DataType;
  description?: string;
}

// ── Row scope discovered for an EAV node inside a Map loop ──
export interface RowScopeInfo {
  mapNodeLabel: string;
  producerNodeId: string;
  producerLabel: string;
  fields: Array<{ name: string; type: DataType }>;
}

// ── Context handed to interpolateVariables at execution time ──
export interface VariableContext {
  /** Output of the most recently executed upstream node */
  lastOutput?: unknown;
  /** Final output per node id, accumulated during traversal */
  nodeOutputs?: Record<string, unknown>;
}

export function getSchemaById(schemaId?: string | null): StepSchema | undefined {
  return schemaId ? schemaById.get(schemaId) : undefined;
}

function nodeSchemaId(node: VariableCanvasNode): string | undefined {
  if (node.data?.schemaId) return node.data.schemaId;
  const t = node.type;
  return t && t.startsWith('stepflow:') ? t : undefined;
}

export function labelOf(node: VariableCanvasNode): string {
  return node.data?.label || 'node';
}

/** "My SQL Query!" → "my_sql_query" */
export function slugify(value: string): string {
  return (value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

/** Parse a comma-separated column declaration into trimmed names. */
export function parseColumns(raw: unknown): string[] {
  if (typeof raw !== 'string') return [];
  const seen = new Set<string>();
  const out: string[] = [];
  for (const part of raw.split(',')) {
    const name = part.trim();
    if (name && !seen.has(name.toLowerCase())) {
      seen.add(name.toLowerCase());
      out.push(name);
    }
  }
  return out;
}

/** Build the variable list exposed by a single node. */
function variablesForNode(node: VariableCanvasNode): InScopeVariable[] {
  const label = labelOf(node);
  const baseSlug = slugify(label) || 'node';
  const vars: InScopeVariable[] = [];
  const seenTokens = new Set<string>();

  const push = (token: string, fieldPath: string[], type: DataType, description?: string) => {
    if (!seenTokens.has(token)) {
      seenTokens.add(token);
      vars.push({ token, nodeLabel: label, nodeId: node.id, fieldPath, type, description });
    }
  };

  // Whole-node output is always available.
  push(baseSlug, [], 'any', 'Entire node output');

  // Declared schema output ports with explicit fields (future-proof).
  const schema = getSchemaById(nodeSchemaId(node));
  for (const out of schema?.outputs ?? []) {
    const fields = out.fields;
    if (!Array.isArray(fields)) continue;
    for (const f of fields) push(`${baseSlug}.${slugify(f.name)}`, [f.name], f.type || 'json', `Output "${out.label}"`);
  }

  // Dynamic columns declared on the instance (SQL / DuckDB result sets).
  const config = node.data?.configuration ?? {};
  for (const col of parseColumns(config.outputColumns)) {
    push(`${baseSlug}.${slugify(col)}`, [col], 'json', 'Output column');
  }

  return vars;
}

/**
 * Collect every variable referenceable from `nodeId` by walking upstream
 * along edges. Nearest ancestors come first.
 */
export function collectInScopeVariables(
  nodeId: string,
  nodes: VariableCanvasNode[],
  edges: Array<{ source: string; target: string }>
): InScopeVariable[] {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  if (!byId.has(nodeId)) return [];

  // target → sources adjacency (self-loops ignored)
  const incoming = new Map<string, string[]>();
  for (const e of edges) {
    if (e.source === e.target) continue;
    if (!byId.has(e.source) || !byId.has(e.target)) continue;
    const list = incoming.get(e.target);
    if (list) list.push(e.source);
    else incoming.set(e.target, [e.source]);
  }

  const vars: InScopeVariable[] = [];
  const visited = new Set<string>([nodeId]);
  const queue: string[] = [nodeId];

  while (queue.length > 0) {
    const current = queue.shift()!;
    for (const sourceId of incoming.get(current) ?? []) {
      if (visited.has(sourceId)) continue;
      visited.add(sourceId);
      const node = byId.get(sourceId)!;
      vars.push(...variablesForNode(node));
      queue.push(sourceId);
    }
  }

  return vars;
}

// ── Interpolation ─────────────────────────────────────────────

function stringifyValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

/**
 * Replace {{token}} placeholders in `template`.
 *
 * Resolution rules:
 *  - First segment is a node id, or the slugified label of an upstream node.
 *  - Remaining segments walk into the object (array indices resolve by number).
 *  - The special head "input" resolves against context.lastOutput.
 *  - Unresolvable tokens are left in place and reported via `unresolved`.
 */
export function interpolateVariables(
  template: string | null | undefined,
  nodes: VariableCanvasNode[],
  context: VariableContext = {}
): { text: string; unresolved: string[] } {
  const source = typeof template === 'string' ? template : '';
  if (!source.includes('{{')) return { text: source, unresolved: [] };

  const byId = new Map(nodes.map((n) => [n.id, n]));
  const slugToId = new Map<string, string>();
  for (const n of nodes) {
    const slug = slugify(labelOf(n));
    if (slug && !slugToId.has(slug)) slugToId.set(slug, n.id);
  }

  const unresolved: string[] = [];

  const text = source.replace(TOKEN_RE, (match, rawToken: string) => {
    const parts = rawToken.trim().split('.').map((p) => p.trim()).filter(Boolean);
    if (parts.length === 0) return match;

    let base: unknown;
    if (parts[0] === 'input') {
      base = context.lastOutput;
    } else {
      const nodeId = byId.has(parts[0]) ? parts[0] : slugToId.get(parts[0]);
      base = nodeId ? context.nodeOutputs?.[nodeId] : undefined;
    }

    if (base === undefined) {
      unresolved.push(rawToken.trim());
      return match;
    }

    let value: unknown = base;
    for (const part of parts.slice(1)) {
      if (value === null || value === undefined) break;
      if (Array.isArray(value)) {
        const idx = Number(part);
        value = Number.isInteger(idx) && idx >= 0 ? value[idx] : undefined;
      } else if (typeof value === 'object') {
        value = (value as Record<string, unknown>)[part];
      } else {
        value = undefined; // path continues into a primitive
      }
    }

    if (value === undefined) {
      unresolved.push(rawToken.trim());
      return match;
    }
    return stringifyValue(value);
  });

  return { text, unresolved };
}

// ── Sub-flow row scope discovery (Map loop → EAV column picker) ──

interface SavedFlowShape {
  id?: string;
  name?: string;
  definition?: { nodes?: VariableCanvasNode[]; edges?: Array<{ source: string; target: string }> };
}

function readSavedFlows(): SavedFlowShape[] {
  try {
    const raw = localStorage.getItem(SAVED_FLOWS_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? (parsed as SavedFlowShape[]) : [];
  } catch {
    return [];
  }
}

/** Walk upstream from `startId` inside a saved flow definition, nearest first. */
function* ancestorNodes(
  startId: string,
  def: NonNullable<SavedFlowShape['definition']>
): Generator<VariableCanvasNode> {
  const byId = new Map((def.nodes ?? []).map((n) => [n.id, n]));
  if (!byId.has(startId)) return;

  const incoming = new Map<string, string[]>();
  for (const e of def.edges ?? []) {
    if (e.source === e.target || !byId.has(e.source) || !byId.has(e.target)) continue;
    const list = incoming.get(e.target);
    if (list) list.push(e.source);
    else incoming.set(e.target, [e.source]);
  }

  const visited = new Set<string>([startId]);
  const queue: string[] = [startId];
  while (queue.length > 0) {
    const current = queue.shift()!;
    for (const sourceId of incoming.get(current) ?? []) {
      if (visited.has(sourceId)) continue;
      visited.add(sourceId);
      yield byId.get(sourceId)!;
      queue.push(sourceId);
    }
  }
}

/**
 * Find the row columns available to an EAV node whose parent flow is a Map
 * loop over `subflowId`. Traces every Map state targeting that sub-flow back
 * to its upstream data source and reads the declared output columns.
 */
export function findSubflowRowFields(subflowId: string): RowScopeInfo | null {
  if (!subflowId) return null;

  for (const flow of readSavedFlows()) {
    const def = flow.definition;
    if (!def || !Array.isArray(def.nodes)) continue;

    const mapNodes = def.nodes.filter(
      (n) => nodeSchemaId(n) === 'stepflow:flow:map' && n.data?.configuration?.targetFlowId === subflowId
    );

    for (const mapNode of mapNodes) {
      for (const ancestor of ancestorNodes(mapNode.id, def)) {
        const columns = parseColumns(ancestor.data?.configuration?.outputColumns);
        if (columns.length > 0) {
          return {
            mapNodeLabel: labelOf(mapNode),
            producerNodeId: ancestor.id,
            producerLabel: labelOf(ancestor),
            fields: columns.map((name) => ({ name, type: 'json' as DataType })),
          };
        }
      }
    }
  }

  return null;
}

/** Id of the flow currently open on the canvas (session-scoped). */
export function getActiveFlowId(): string | null {
  try {
    return sessionStorage.getItem(ACTIVE_FLOW_STORAGE_KEY);
  } catch {
    return null;
  }
}
