import { describe, it, expect, beforeEach } from 'vitest';
import { FlowService } from '@services/flowService';
import { useNodeStore } from '@stores/useNodeStore';
import { mapStateSchema } from '@schemas/steps/flow';

const FLOWS_KEY = 'stepflow-flows';

interface SavedFlow {
  id: string;
  name: string;
  definition: unknown;
}

function seedFlows(flows: SavedFlow[]) {
  localStorage.setItem(FLOWS_KEY, JSON.stringify(flows));
}

function readFlows(): SavedFlow[] {
  const raw = localStorage.getItem(FLOWS_KEY);
  return raw ? (JSON.parse(raw) as SavedFlow[]) : [];
}

describe('Map iterator body scaffolding (ensureIteratorBody)', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('creates a canonical pre-wired starter body in graph format', async () => {
    useNodeStore.getState().addNode('stepflow:flow:map', { x: 0, y: 0 });
    const mapId = useNodeStore.getState().nodes[0].id;

    const res = await FlowService.ensureIteratorBody(mapId);

    expect(res.success).toBe(true);
    expect(res.created).toBe(true);
    expect(res.flowId).toBeTruthy();
    expect(res.flowName).toBe('Map Iterator');
    expect(res.message).toContain('linked it to the Map node');

    // The Map node now points at the new body.
    const map = useNodeStore.getState().nodes.find((n) => n.id === mapId)!;
    expect(map.data.configuration?.targetFlowId).toBe(res.flowId);

    // Saved body must be graph format (exact schema ids survive a round-trip).
    const body = readFlows().find((f) => f.id === res.flowId);
    expect(body).toBeDefined();
    const def = body!.definition as {
      nodes: Array<{ schemaId: string; label: string }>;
      edges: Array<{ source: string; target: string }>;
    };
    expect(def.nodes.map((n) => n.schemaId)).toEqual([
      'stepflow:utility:pass', // start (not a Start-state type — ASL export needs that distinction)
      'stepflow:data:eav',
      'stepflow:ai:text',
      'stepflow:ai:decision',
    ]);
    expect(def.edges).toEqual([
      { source: 'Iteration Start', target: 'Read Row' },
      { source: 'Read Row', target: 'Generate Text' },
      { source: 'Generate Text', target: 'Decide' },
    ]);

    // Key starter configs are present.
    const eav = def.nodes.find((n) => n.schemaId === 'stepflow:data:eav')!;
    expect((eav as Record<string, unknown>).config).toMatchObject({ operation: 'read' });
  });

  it('is idempotent when the Map is already linked', async () => {
    useNodeStore.getState().addNode('stepflow:flow:map', { x: 0, y: 0 });
    const mapId = useNodeStore.getState().nodes[0].id;

    const first = await FlowService.ensureIteratorBody(mapId);
    expect(first.created).toBe(true);

    const second = await FlowService.ensureIteratorBody(mapId);
    expect(second.success).toBe(true);
    expect(second.created).toBe(false);
    expect(second.flowId).toBe(first.flowId);
    expect(second.message).toContain('already linked');

    // Still exactly one body in storage.
    const flows = readFlows().filter((f) => f.id === first.flowId);
    expect(flows).toHaveLength(1);
  });

  it('honors a caller-supplied initial definition', async () => {
    useNodeStore.getState().addNode('stepflow:flow:map', { x: 0, y: 0 });
    const mapId = useNodeStore.getState().nodes[0].id;
    const custom = { nodes: [{ schemaId: 'stepflow:utility:pass', label: 'Only' }], edges: [] };

    const res = await FlowService.ensureIteratorBody(mapId, { initialDefinition: custom });
    expect(res.success).toBe(true);
    expect(res.created).toBe(true);

    const body = readFlows().find((f) => f.id === res.flowId)!;
    expect(body.definition).toEqual(custom);
  });

  it('rejects non-Map nodes', async () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const passId = useNodeStore.getState().nodes[0].id;

    const res = await FlowService.ensureIteratorBody(passId);
    expect(res.success).toBe(false);
    expect(res.created).toBe(false);
  });
});

describe('Map validation: iterator-flow-exists', () => {
  const rule = mapStateSchema.validation.find((r) => r.id === 'iterator-flow-exists');

  beforeEach(() => {
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('exists on the Map schema', () => {
    expect(rule).toBeDefined();
  });

  it('passes when the linked flow exists in storage', () => {
    seedFlows([{ id: 'flow-abc123', name: 'Body', definition: { nodes: [], edges: [] } }]);
    const res = rule!.check(
      { schemaId: mapStateSchema.schemaId, label: 'Map', configuration: { targetFlowId: 'flow-abc123' } },
      new Set(),
    );
    expect(res.isValid).toBe(true);
  });

  it('fails when the linked flow is missing from storage', () => {
    seedFlows([]);
    const res = rule!.check(
      { schemaId: mapStateSchema.schemaId, label: 'Map', configuration: { targetFlowId: 'flow-missing' } },
      new Set(),
    );
    expect(res.isValid).toBe(false);
    expect(res.reason).toMatch(/missing from saved flows/);
  });

  it('passes when no flow is linked yet (the required-field rule covers that case)', () => {
    const res = rule!.check(
      { schemaId: mapStateSchema.schemaId, label: 'Map', configuration: {} },
      new Set(),
    );
    expect(res.isValid).toBe(true);
  });

});
