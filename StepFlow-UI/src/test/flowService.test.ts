import { describe, it, expect, beforeEach } from 'vitest';
import { FlowService } from '@services/flowService';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';

describe('FlowService', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });
  });

  describe('exportFlow', () => {
    it('should export empty flow', () => {
      const result = FlowService.exportFlow();
      expect(result).toEqual({
        startAt: '',
        states: {},
      });
    });

    it('should export flow with nodes', () => {
      useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
      useNodeStore.getState().addNode('stepflow:ai:decision', { x: 0, y: 150 });

      const result = FlowService.exportFlow();
      expect(Object.keys(result.states)).toHaveLength(2);
      expect(result.startAt).toBeTruthy();
    });

    it('should include connections in export', () => {
      useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
      useNodeStore.getState().addNode('stepflow:ai:decision', { x: 0, y: 150 });

      const node1 = useNodeStore.getState().nodes[0];
      const node2 = useNodeStore.getState().nodes[1];

      useEdgeStore.getState().addEdge({
        id: 'edge-1',
        source: node1.id,
        target: node2.id,
      });

      const result = FlowService.exportFlow();
      expect(result.states[node1.id].next).toBe(node2.id);
    });

    it('should include resource URIs', () => {
      useNodeStore.getState().addNode('stepflow:ai:decision', { x: 0, y: 0 });
      useNodeStore.getState().updateNodeData(
        useNodeStore.getState().nodes[0].id,
        { configuration: { llmService: 'azureOpenAI' } }
      );

      const result = FlowService.exportFlow();
      const state = Object.values(result.states)[0];
      expect(state.resource).toContain('ai://');
    });
  });

  describe('saveFlow / listFlows', () => {
    beforeEach(() => {
      // localStorage may not be available in jsdom without --localstorage-file
      try {
        localStorage.removeItem('stepflow-flows');
      } catch { /* ignore */ }
    });

    it.skip('should save and list flows', async () => {
      useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
      await FlowService.saveFlow('Test Flow', 'A test flow');

      const flows = await FlowService.listFlows();
      expect(flows.length).toBeGreaterThanOrEqual(1);
      expect(flows.find((f) => f.name === 'Test Flow')).toBeDefined();
    });

    it.skip('should load a saved flow', async () => {
      useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
      await FlowService.saveFlow('Loadable Flow');

      const flows = await FlowService.listFlows();
      const flow = flows.find((f) => f.name === 'Loadable Flow');
      expect(flow).toBeDefined();

      const loaded = await FlowService.loadFlow(flow!.id);
      expect(loaded).toBeDefined();
      expect(loaded?.states).toBeDefined();
    });
  });
  describe('importFlow', () => {
    it('imports an ASL capture/save flow with the right node types and edges', () => {
      FlowService.importFlow({
        startAt: 'CaptureOrder',
        states: {
          CaptureOrder: { type: 'FormCapture', task: { formId: 'order-intake', title: 'Order Intake' }, next: 'SaveOrder' },
          SaveOrder: { type: 'Task', resource: 'eav://OrderIntake', parameters: { operation: 'write', entityType: 'OrderIntake' }, next: 'Done' },
          Done: { type: 'Succeed' },
        },
      });

      const nodes = useNodeStore.getState().nodes;
      expect(nodes).toHaveLength(3);
      expect(nodes.map((n) => n.data?.schemaId)).toEqual([
        'stepflow:formcapture:capture',
        'stepflow:data:eav',
        'stepflow:utility:pass',
      ]);
      expect(nodes[0].data?.configuration).toMatchObject({ formId: 'order-intake' });
      expect(nodes[1].data?.configuration).toMatchObject({ operation: 'write', entityType: 'OrderIntake' });

      const edges = useEdgeStore.getState().edges;
      expect(edges).toHaveLength(2);
    });

    it('maps https:// resources back to the HTTP node (round-trip)', () => {
      FlowService.importFlow({
        startAt: 'Save',
        states: {
          Save: { type: 'Task', resource: 'https://api.example.com/orders', parameters: { method: 'POST', url: 'https://api.example.com/orders' } },
        },
      });

      const node = useNodeStore.getState().nodes[0];
      expect(node.data?.schemaId).toBe('stepflow:api:http');
      expect(node.data?.configuration).toMatchObject({ method: 'POST', url: 'https://api.example.com/orders' });
    });
  });
});
