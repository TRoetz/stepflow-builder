import { describe, it, expect, beforeEach } from 'vitest';
import { FlowService } from '@services/flowService';
import { useNodeStore } from '@stores/useNodeStore';
import { flowTemplates } from '@schemas/templates';

const FLOWS_KEY = 'stepflow-flows';

function readFlows(): Array<{ id: string; name: string; definition: unknown }> {
  const raw = localStorage.getItem(FLOWS_KEY);
  return raw ? (JSON.parse(raw) as never) : [];
}

describe('Canonical flow template — EAV Row Processing (P4)', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('is present in the template catalogue', () => {
    expect(flowTemplates.some((t) => t.id === 'tpl-eav-row-processing')).toBe(true);
  });

  it('instantiates the full chain and links an exact-schema iterator body to the Map node', async () => {
    const res = await FlowService.instantiateTemplate('tpl-eav-row-processing');
    expect(res.success).toBe(true);
    expect(res.message).toContain('Iterator Body');

    // Main chain: Start → SQL Query → Map → Rule Engine → Script → End
    const nodes = useNodeStore.getState().nodes;
    expect(nodes.map((n) => n.data.schemaId)).toEqual([
      'stepflow:terminal:start',
      'stepflow:data:sql',
      'stepflow:flow:map',
      'stepflow:rule:rule_engine',
      'stepflow:transform:script',
      'stepflow:terminal:end',
    ]);

    // The Map node is linked to a saved sub-flow…
    const map = nodes.find((n) => n.data.schemaId === 'stepflow:flow:map')!;
    const bodyId = String(map.data.configuration?.targetFlowId ?? '');
    expect(bodyId).toBeTruthy();

    // …whose definition keeps the exact AI schema ids (no collapse to a generic decision node on round-trip).
    const body = readFlows().find((f) => f.id === bodyId);
    expect(body).toBeDefined();
    const def = body!.definition as { nodes: Array<{ schemaId: string; label: string }> };
    expect(def.nodes.map((n) => n.schemaId)).toEqual([
      'stepflow:utility:pass', // start (not a Start-state type — ASL export needs that distinction)
      'stepflow:data:eav',
      'stepflow:ai:text',
      'stepflow:ai:decision',
      'stepflow:terminal:end', // terminal so the engine's inner execution can terminate each iteration
    ]);
  });

  it('rejects unknown template ids without touching the canvas', async () => {
    const res = await FlowService.instantiateTemplate('tpl-does-not-exist');
    expect(res.success).toBe(false);
    expect(useNodeStore.getState().nodes).toHaveLength(0);
  });
});

describe('Flow template — DataExchange Pipeline', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('is present in the template catalogue', () => {
    expect(flowTemplates.some((t) => t.id === 'tpl-data-exchange-pipeline')).toBe(true);
  });

  it('instantiates Start → Build Rows → Exchange → Summarize → End with the demo profile wired', async () => {
    const res = await FlowService.instantiateTemplate('tpl-data-exchange-pipeline');
    expect(res.success).toBe(true);

    const nodes = useNodeStore.getState().nodes;
    expect(nodes.map((n) => n.data.schemaId)).toEqual([
      'stepflow:terminal:start',
      'stepflow:transform:jsonata',
      'stepflow:data:exchange',
      'stepflow:transform:jsonata',
      'stepflow:terminal:end',
    ]);

    const exchange = nodes.find((n) => n.data.schemaId === 'stepflow:data:exchange')!;
    expect(exchange.data.configuration).toMatchObject({ profileId: 'demo-order-validation' });

    // The row-builder node must emit the { rows: [...] } envelope the executor ingests.
    const builder = nodes[1];
    expect(String(builder.data.configuration?.expression ?? '')).toContain('rows');
  });
});

describe('Flow template — Dynamic API Round Trip', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('is present in the template catalogue', () => {
    expect(flowTemplates.some((t) => t.id === 'tpl-dynamic-api-roundtrip')).toBe(true);
  });

  it('instantiates Create API → Save Record → Read Record → Verify with POST bodies wired', async () => {
    const res = await FlowService.instantiateTemplate('tpl-dynamic-api-roundtrip');
    expect(res.success).toBe(true);

    const nodes = useNodeStore.getState().nodes;
    expect(nodes.map((n) => n.data.schemaId)).toEqual([
      'stepflow:terminal:start',
      'stepflow:api:http',
      'stepflow:api:http',
      'stepflow:api:http',
      'stepflow:transform:jsonata',
      'stepflow:terminal:end',
    ]);

    const [createApi, saveRecord, readRecord] = nodes.slice(1, 4);
    expect(createApi.data.configuration).toMatchObject({ method: 'POST', url: 'http://localhost:5001/api/dynamic/apis' });
    expect(String(createApi.data.configuration?.body ?? '')).toContain('demo-records');

    expect(saveRecord.data.configuration).toMatchObject({ method: 'POST', url: 'http://localhost:5001/api/dynamic/demo-records' });
    expect(String(saveRecord.data.configuration?.body ?? '')).toContain('rec-demo-1');

    expect(readRecord.data.configuration).toMatchObject({
      method: 'GET',
      url: 'http://localhost:5001/api/dynamic/demo-records?entityId=rec-demo-1',
    });
  });
});

describe('Flow template — Human Task Approval', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    try {
      localStorage.removeItem(FLOWS_KEY);
    } catch {
      /* storage unavailable */
    }
  });

  it('is present in the template catalogue', () => {
    expect(flowTemplates.some((t) => t.id === 'tpl-human-task-approval')).toBe(true);
  });

  it('instantiates Prepare → Human Task → Record Decision with assignee wired', async () => {
    const res = await FlowService.instantiateTemplate('tpl-human-task-approval');
    expect(res.success).toBe(true);

    const nodes = useNodeStore.getState().nodes;
    expect(nodes.map((n) => n.data.schemaId)).toEqual([
      'stepflow:terminal:start',
      'stepflow:transform:jsonata',
      'stepflow:human:task',
      'stepflow:transform:jsonata',
      'stepflow:terminal:end',
    ]);

    const task = nodes.find((n) => n.data.schemaId === 'stepflow:human:task')!;
    expect(task.data.configuration).toMatchObject({ assignee: 'finance-team' });
    expect(String(task.data.configuration?.taskTitle ?? '')).toContain('REQ-001');
  });
});
