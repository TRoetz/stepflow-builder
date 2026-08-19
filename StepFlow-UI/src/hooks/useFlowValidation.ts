import { useMemo } from 'react';
import { useNodeStore, StepNode } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { schemaById } from '@schemas/index';
import type { NodeData } from '@schema-types/schema';

export interface FlowIssue {
  nodeId: string;
  label: string;
  errors: string[];
}

type EdgeLike = { source: string; target: string };

/** Schema id of the AI Text Generator step (lineage hint applies here). */
const TEXT_GEN_SCHEMA_ID = 'stepflow:ai:text';

/** Matches {{…}} variable tokens used by the P1 lineage picker. */
const VARIABLE_TOKEN_RE = /\{\{[^{}]*\}\}/;

/**
 * Validate a single node against its schema: required configuration fields plus
 * the schema's validation rules. Returns human-readable error strings (empty = valid).
 * Pure — shared by the flow-health hook and the per-node badges in BaseNode.
 */
export function getConfigIssues(data: NodeData | undefined): string[] {
  if (!data?.schemaId) return [];

  const schema = schemaById.get(data.schemaId);
  if (!schema) return [];

  const errors: string[] = [];

  // Required configuration fields.
  for (const field of schema.configFields ?? []) {
    if (!field.required) continue;
    const value = data.configuration?.[field.id];
    if (value === undefined || value === null || value === '') {
      errors.push(`"${field.label}" is required`);
    }
  }

  // Schema validation rules.
  for (const rule of schema.validation ?? []) {
    try {
      const result = rule.check(data, new Set());
      if (!result.isValid && result.reason) {
        errors.push(result.reason);
      }
    } catch {
      /* malformed rule — ignore */
    }
  }

  return errors;
}

/**
 * Lineage-aware hints that need the full graph (node + edges), e.g. "AI Text Gen
 * prompt references no row variables" for a generator fed by upstream data whose
 * prompt contains no {{…}} tokens. First nodes have nothing to reference, so they
 * are exempt. Pure — shared by the flow-health hook and BaseNode badges.
 */
export function getGraphHintsForNode(
  nodeId: string,
  nodes: StepNode[],
  edges: EdgeLike[]
): string[] {
  const node = nodes.find((n) => n.id === nodeId);
  const data = node?.data as NodeData | undefined;
  if (!data || data.schemaId !== TEXT_GEN_SCHEMA_ID) return [];

  // Only meaningful when the generator is fed by upstream output.
  if (!edges.some((e) => e.target === nodeId)) return [];

  const prompt = String(data.configuration?.systemPrompt ?? '');
  if (!prompt.trim()) return []; // empty prompt already surfaces as a config issue
  if (VARIABLE_TOKEN_RE.test(prompt)) return [];

  return ['Prompt references no upstream {{…}} variables'];
}

/**
 * Aggregate all issues for a flow: one entry per invalid node.
 * Pure — used by useFlowValidation and tests.
 */
export function collectFlowIssues(nodes: StepNode[], edges: EdgeLike[]): FlowIssue[] {
  const issues = new Map<string, string[]>();
  for (const node of nodes) {
    const errors = [...getConfigIssues(node.data as NodeData | undefined), ...getGraphHintsForNode(node.id, nodes, edges)];
    if (errors.length > 0) issues.set(node.id, errors);
  }
  return nodes
    .filter((node) => issues.has(node.id))
    .map((node) => ({
      nodeId: node.id,
      label: (node.data as NodeData | undefined)?.label ?? 'Step',
      errors: issues.get(node.id)!,
    }));
}

/**
 * Validate every node on the canvas against its schema plus graph-level hints.
 * Returns one issue entry per invalid node (empty array = flow is valid).
 */
export function useFlowValidation(): FlowIssue[] {
  const nodes = useNodeStore((s) => s.nodes);
  const edges = useEdgeStore((s) => s.edges);

  return useMemo(() => collectFlowIssues(nodes, edges), [nodes, edges]);
}
