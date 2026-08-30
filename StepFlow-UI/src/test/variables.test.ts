/**
 * Tests for the unified {{variable}} engine (src/utils/variables.ts).
 */
import { afterEach, describe, expect, it } from 'vitest';
import {
  collectInScopeVariables,
  findSubflowRowFields,
  interpolateVariables,
  slugify,
} from '@utils/variables';
import type { VariableCanvasNode } from '@utils/variables';

const nodes: VariableCanvasNode[] = [
  { id: 'sql_q', data: { label: 'SQL Query' } },    // slug: sql_query
  { id: 'ai_summ', data: { label: 'AI Summary' } }, // slug: ai_summary
  { id: 'down', data: { label: 'Downstream' } },
];

const edges = [
  { source: 'sql_q', target: 'ai_summ' },
  { source: 'ai_summ', target: 'down' },
];

describe('slugify', () => {
  it('normalizes labels to lowercase dash-separated slugs', () => {
    expect(slugify('SQL Query')).toBe('sql_query');
    expect(slugify('  My--Node!! x ')).toBe('my_node_x');
  });
});

describe('interpolateVariables', () => {
  it('resolves whole-node output by id and by label slug', () => {
    const ctx = { lastOutput: null, nodeOutputs: { sql_q: { ok: true } } };
    expect(interpolateVariables('{{sql_q}}', nodes, ctx).text).toBe('{"ok":true}');
    // The label-slug form must resolve to the same node.
    expect(interpolateVariables('{{sql_query.ok}}', nodes, ctx).text).toBe('true');
  });

  it('resolves {{input}} against lastOutput and walks array indices', () => {
    const ctx = { lastOutput: { rows: [{ value: 7 }] }, nodeOutputs: {} };
    expect(interpolateVariables('got={{input.rows.0.value}}', nodes, ctx).text).toBe('got=7');
  });

  it('leaves unresolvable tokens in place and reports them', () => {
    const r = interpolateVariables('{{missing.x}} and {{sql_q.nope}}', nodes, {
      lastOutput: null,
      nodeOutputs: { sql_q: {} },
    });
    expect(r.text).toBe('{{missing.x}} and {{sql_q.nope}}');
    expect(r.unresolved).toEqual(['missing.x', 'sql_q.nope']);
  });

  it('returns non-template strings untouched with no unresolved tokens', () => {
    const r = interpolateVariables('plain text', nodes, {});
    expect(r.text).toBe('plain text');
    expect(r.unresolved).toEqual([]);
  });
});

describe('collectInScopeVariables', () => {
  it('lists variables from all upstream ancestors, nearest first', () => {
    const vars = collectInScopeVariables('down', nodes, edges);
    // Nearest ancestor (ai_summ) must come before the next one up (sql_q),
    // and every node contributes at least its whole-node token.
    expect(Array.from(new Set(vars.map((v) => v.nodeId)))).toEqual(['ai_summ', 'sql_q']);
    const first = vars[0];
    expect(first.token).toBe('ai_summary');
    expect(first.fieldPath).toEqual([]); // whole-node entry
  });

  it('ignores self-loops and edges touching unknown nodes', () => {
    const vars = collectInScopeVariables(
      'down',
      [...nodes, { id: 'ghost', data: { label: 'Ghost' } }],
      [
        ...edges,
        { source: 'down', target: 'down' },
        { source: 'nope', target: 'down' },
      ]
    );
    expect(Array.from(new Set(vars.map((v) => v.nodeId)))).toEqual(['ai_summ', 'sql_q']);
  });

  it('includes dynamic output columns declared on the instance', () => {
    const withCols = nodes.map((n) =>
      n.id === 'sql_q' ? { ...n, data: { ...n.data, configuration: { outputColumns: 'name, age' } } } : n
    );
    const vars = collectInScopeVariables('down', withCols, edges);
    expect(vars.some((v) => v.token === 'sql_query.name')).toBe(true);
    expect(vars.some((v) => v.token === 'sql_query.age')).toBe(true);
  });

  it('returns [] for an unknown node id', () => {
    expect(collectInScopeVariables('missing', nodes, edges)).toEqual([]);
  });
});

describe('findSubflowRowFields', () => {
  afterEach(() => window.localStorage.clear());

  it('maps a Map loop to the upstream producer with output columns (deduped case-insensitively)', () => {
    const sub = 'sub-flow-1';
    const def = {
      nodes: [
        {
          id: 'producer',
          data: { label: 'Producer', schemaId: 'stepflow:data:eav', configuration: { outputColumns: 'name, age , name' } },
        },
        { id: 'mapNode', data: { label: 'Loop', schemaId: 'stepflow:flow:map', configuration: { targetFlowId: sub } } },
      ],
      edges: [{ source: 'producer', target: 'mapNode' }],
    };
    window.localStorage.setItem('stepflow-flows', JSON.stringify([{ id: sub, name: 'Sub', definition: def }]));

    const info = findSubflowRowFields(sub);
    expect(info?.fields.map((f) => f.name)).toEqual(['name', 'age']);
    expect(info?.mapNodeLabel).toBe('Loop');
    expect(info?.producerNodeId).toBe('producer');
  });

  it('returns null when no Map node targets the sub-flow', () => {
    window.localStorage.setItem(
      'stepflow-flows',
      JSON.stringify([{ id: 'other', name: 'Other', definition: { nodes: [], edges: [] } }])
    );
    expect(findSubflowRowFields('sub-x')).toBeNull();
  });
});
