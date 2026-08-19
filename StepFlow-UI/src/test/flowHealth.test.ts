import { describe, it, expect } from 'vitest';
import {
  collectFlowIssues,
  getConfigIssues,
  getGraphHintsForNode,
} from '@hooks/useFlowValidation';
import type { StepNode } from '@stores/useNodeStore';
import type { NodeData } from '@schema-types/schema';

const node = (
  id: string,
  schemaId: string,
  configuration: Record<string, unknown>,
): StepNode =>
  ({
    id,
    data: { label: id, schemaId, configuration } as NodeData,
  }) as unknown as StepNode;

describe('Flow health hints (P5)', () => {
  it('flags a Map with no iterator flow selected', () => {
    const map = node('map1', 'stepflow:flow:map', { targetFlowId: '', itemsPath: '$.items' });
    const issues = collectFlowIssues([map], []);
    expect(issues).toHaveLength(1);
    expect(issues[0].nodeId).toBe('map1');
    expect(issues[0].errors.join(' ')).toContain('Iterator Flow (Sub-Flow)');
  });

  it('does not flag a Map with an iterator flow selected (when that flow exists)', () => {
    // The P3 body-integrity rule checks the linked flow against saved flows.
    localStorage.setItem('stepflow-flows', JSON.stringify([{ id: 'f1' }]));
    try {
      const map = node('map1', 'stepflow:flow:map', { targetFlowId: 'f1', itemsPath: '$.items' });
      expect(getConfigIssues(map.data as NodeData)).toEqual([]);
    } finally {
      localStorage.removeItem('stepflow-flows');
    }
  });

  it('flags a Rule Engine with zero branches (empty rule set)', () => {
    const engine = node('rule1', 'stepflow:rule:rule_engine', { ruleSet: '[]' });
    const errors = getConfigIssues(engine.data as NodeData);
    expect(errors).toContain('Rule set must be a non-empty JSON array');
  });

  it('does not flag a Rule Engine with at least one branch', () => {
    const engine = node('rule1', 'stepflow:rule:rule_engine', {
      ruleSet: JSON.stringify([{ name: 'default', expression: 'true', outcome: 'pass' }]),
    });
    expect(getConfigIssues(engine.data as NodeData)).toEqual([]);
  });

  describe('AI Text Gen prompt lineage hint', () => {
    const gen = (prompt: string) =>
      node('gen1', 'stepflow:ai:text', {
        model: 'gpt-4o',
        systemPrompt: prompt,
        temperature: 0.7,
      });

    it('hints when a fed generator prompt references no {{…}} variables', () => {
      const nodes = [node('sql1', 'stepflow:data:sql', {}), gen('Summarize this row.')];
      const edges = [{ source: 'sql1', target: 'gen1' }];
      expect(getGraphHintsForNode('gen1', nodes, edges)).toEqual([
        'Prompt references no upstream {{…}} variables',
      ]);
      const issues = collectFlowIssues(nodes, edges);
      const genIssue = issues.find((i) => i.nodeId === 'gen1');
      expect(genIssue?.errors).toContain('Prompt references no upstream {{…}} variables');
    });

    it('does not hint when the prompt uses variable tokens', () => {
      const nodes = [node('sql1', 'stepflow:data:sql', {}), gen('Summarize {{row.name}}.')];
      expect(getGraphHintsForNode('gen1', nodes, [{ source: 'sql1', target: 'gen1' }])).toEqual([]);
    });

    it('does not hint for a generator with no upstream edge (first node)', () => {
      const nodes = [gen('Summarize this row.')];
      expect(getGraphHintsForNode('gen1', nodes, [])).toEqual([]);
    });

    it('does not hint when the prompt is empty (config issue already covers it)', () => {
      const nodes = [node('sql1', 'stepflow:data:sql', {}), gen('')];
      expect(getGraphHintsForNode('gen1', nodes, [{ source: 'sql1', target: 'gen1' }])).toEqual([]);
    });
  });

  it('aggregates per-node issues for mixed flows (header indicator parity)', () => {
    const nodes = [
      node('map1', 'stepflow:flow:map', { targetFlowId: '', itemsPath: '$.items' }),
      node('ok1', 'stepflow:ai:text', {
        model: 'gpt-4o',
        systemPrompt: 'Hello {{name}}',
        temperature: 0.7,
      }),
    ];
    const issues = collectFlowIssues(nodes, []);
    expect(issues.map((i) => i.nodeId)).toEqual(['map1']);
  });
});
