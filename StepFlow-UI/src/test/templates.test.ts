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
    ]);
  });

  it('rejects unknown template ids without touching the canvas', async () => {
    const res = await FlowService.instantiateTemplate('tpl-does-not-exist');
    expect(res.success).toBe(false);
    expect(useNodeStore.getState().nodes).toHaveLength(0);
  });
});
