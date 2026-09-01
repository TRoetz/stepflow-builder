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

    it('exports an exchange node as a parameterless Task on dataexchange://', () => {
      useNodeStore.getState().addNode('stepflow:data:exchange', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = { profileId: 'demo-order-validation' };

      const result = FlowService.exportFlow();
      expect(result.states[node.id]).toMatchObject({
        type: 'Task',
        resource: 'dataexchange://demo-order-validation',
      });
      // No Parameters at all — the C# executor ingests the upstream output directly.
      expect(result.states[node.id].parameters).toBeUndefined();
    });

    it('exports an HTTP POST with a JSON body as the structured __handler contract', () => {
      useNodeStore.getState().addNode('stepflow:api:http', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = {
        method: 'POST',
        url: 'http://localhost:5001/api/dynamic/apis',
        body: '{"id":"demo-records","name":"Demo Records"}',
      };

      const result = FlowService.exportFlow();
      expect(result.states[node.id]).toMatchObject({
        type: 'Task',
        resource: 'http://localhost:5001/api/dynamic/apis',
        parameters: { __handler: 'http', method: 'POST', body: { id: 'demo-records', name: 'Demo Records' } },
      });
    });

    it('exports a terminal end node as Pass + end:true without resource or parameters', () => {
      useNodeStore.getState().addNode('stepflow:terminal:start', { x: 0, y: 0 });
      useNodeStore.getState().addNode('stepflow:terminal:end', { x: 0, y: 150 });
      const [start, end] = useNodeStore.getState().nodes;
      useEdgeStore.getState().addEdge({ id: 'edge-end', source: start.id, target: end.id, type: 'step-edge' });

      const result = FlowService.exportFlow();
      // The engine has no End state type — termination is signaled by the `end` flag on a Pass.
      expect(result.states[end.id]).toMatchObject({ type: 'Pass', end: true });
      expect(result.states[end.id].resource).toBeUndefined();
      expect(result.states[end.id].parameters).toBeUndefined();
    });

    it('exports a jsonata node with input_data pass-through for engine evaluation', () => {
      useNodeStore.getState().addNode('stepflow:transform:jsonata', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = { expression: '{ processed: $.rowsOut }' };

      const result = FlowService.exportFlow();
      expect(result.states[node.id]).toMatchObject({
        type: 'Task',
        resource: 'transform://jsonata',
        parameters: { expression: '{ processed: $.rowsOut }', 'input_data.$': '$' },
      });
    });
    it('exports an eav write node with values pass-through for engine persistence', () => {
      useNodeStore.getState().addNode('stepflow:data:eav', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = { operation: 'write', entityType: 'refund_decision' };

      const result = FlowService.exportFlow();
      expect(result.states[node.id]).toMatchObject({
        type: 'Task',
        resource: 'eav://refund_decision',
        parameters: { operation: 'write', entityType: 'refund_decision', 'values.$': '$' },
      });
    });
    it('compiles a graph-format iterator body into executable ASL on export', () => {
      // Seed a saved sub-flow in graph format — the shape ensureIteratorBody persists.
      const subFlowId = 'flow-iterator-test';
      localStorage.setItem(
        'stepflow-flows',
        JSON.stringify([
          {
            id: subFlowId,
            name: 'Test Iterator Body',
            createdAt: new Date().toISOString(),
            definition: {
              nodes: [
                { schemaId: 'stepflow:utility:pass', label: 'Iteration Start' },
                { schemaId: 'stepflow:data:eav', label: 'Read Row', config: { operation: 'read', entityType: 'test_entity' } },
              ],
              edges: [{ source: 'Iteration Start', target: 'Read Row' }],
            },
          },
        ])
      );

      useNodeStore.getState().addNode('stepflow:terminal:start', { x: 0, y: 0 });
      const map = useNodeStore.getState().addNode('stepflow:flow:map', { x: 0, y: 150 });
      if (map) map.data.configuration = { itemsPath: '$.rows', targetFlowId: subFlowId };

      const result = FlowService.exportFlow();
      const mapState = result.states[map!.id];
      expect(mapState.type).toBe('Map');

      // The iterator must be compiled ASL ({startAt, states}), not the raw graph format.
      const iter = mapState.iterator!;
      expect(iter.startAt).toBeTruthy();
      expect(iter.states).toBeDefined();
      expect(Object.keys(iter)).not.toContain('nodes');
      expect(Object.keys(iter)).not.toContain('edges');

      // Sub-flow nodes compiled into states with correct wiring and resource URIs.
      const iterStates = Object.values(iter.states);
      expect(iterStates).toHaveLength(2);
      const readRow = iterStates.find((s) => s.resource === 'eav://test_entity');
      expect(readRow?.type).toBe('Task');
      expect(readRow?.parameters).toMatchObject({ operation: 'read', entityType: 'test_entity' });
    });

    it('exports an unlinked Map node with the placeholder iterator', () => {
      useNodeStore.getState().addNode('stepflow:terminal:start', { x: 0, y: 0 });
      const map = useNodeStore.getState().addNode('stepflow:flow:map', { x: 0, y: 150 });
      if (map) map.data.configuration = { itemsPath: '$.rows' };

      const result = FlowService.exportFlow();
      expect(result.states[map!.id].iterator).toEqual({
        startAt: 'PassThrough',
        states: { PassThrough: { type: 'Pass', comment: 'Placeholder iterator flow. Please select a valid target flow.' } },
      });
    });
    it('exports a choice node as an ASL Choice state with expression, next and default', () => {
      useNodeStore.getState().addNode('stepflow:terminal:start', { x: 0, y: 0 });
      useNodeStore.getState().addNode('stepflow:api:http', { x: 0, y: 150 });
      useNodeStore.getState().addNode('stepflow:flow:choice', { x: 0, y: 300 });
      useNodeStore.getState().addNode('stepflow:transform:jsonata', { x: -200, y: 450 });
      useNodeStore.getState().addNode('stepflow:transform:jsonata', { x: 200, y: 450 });
      const [start, http, choice, ok, issue] = useNodeStore.getState().nodes;
      http.data.configuration = { method: 'GET', url: 'https://example.com/health', includeStatus: true };
      choice.data.configuration = { condition: '$.status < 400' };

      const addEdge = useEdgeStore.getState().addEdge;
      addEdge({ id: 'e1', source: start.id, target: http.id });
      addEdge({ id: 'e2', source: http.id, target: choice.id });
      addEdge({ id: 'e3', source: choice.id, target: ok.id, sourceHandle: 'output_true' });
      addEdge({ id: 'e4', source: choice.id, target: issue.id, sourceHandle: 'output_false' });

      const result = FlowService.exportFlow();
      expect(result.states[choice.id]).toMatchObject({
        type: 'Choice',
        choices: [{ expression: '$.status < 400', next: ok.id }],
        default: issue.id,
      });
      // includeStatus must reach the engine so live mode wraps {status, ok, body}.
      expect(result.states[http.id]).toMatchObject({ resource: 'https://example.com/health' });
      expect(result.states[http.id].parameters).toMatchObject({ __handler: 'http', method: 'GET', includeStatus: true });
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
    it('maps dataexchange:// resources back to the exchange node (round-trip)', () => {
      FlowService.importFlow({
        startAt: 'Exchange',
        states: { Exchange: { type: 'Task', resource: 'dataexchange://demo-order-validation' } },
      });

      const node = useNodeStore.getState().nodes[0];
      expect(node.data?.schemaId).toBe('stepflow:data:exchange');
      expect(node.data?.configuration).toMatchObject({ profileId: 'demo-order-validation' });
    });

    it('round-trips an HTTP POST body from the __handler contract back to node config', () => {
      FlowService.importFlow({
        startAt: 'Save',
        states: {
          Save: {
            type: 'Task',
            resource: 'http://localhost:5001/api/dynamic/apis',
            parameters: { __handler: 'http', method: 'POST', body: { id: 'demo-records' } },
          },
        },
      });

      const node = useNodeStore.getState().nodes[0];
      expect(node.data?.schemaId).toBe('stepflow:api:http');
      expect(node.data?.configuration).toMatchObject({ method: 'POST', url: 'http://localhost:5001/api/dynamic/apis' });
      expect(JSON.parse(String(node.data?.configuration?.body))).toEqual({ id: 'demo-records' });
    });

    it('round-trips a jsonata node and strips template-resolution markers from config', () => {
      useNodeStore.getState().addNode('stepflow:transform:jsonata', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = { expression: '{ processed: $.rowsOut }' };

      const exported = FlowService.exportFlow();
      expect(exported.states[node.id].parameters).toMatchObject({ 'input_data.$': '$' });

      useNodeStore.setState({ nodes: [] });
      useEdgeStore.setState({ edges: [] });
      FlowService.importFlow(exported);

      const imported = useNodeStore.getState().nodes[0];
      expect(imported.data?.schemaId).toBe('stepflow:transform:jsonata');
      expect(imported.data?.configuration).toEqual({ expression: '{ processed: $.rowsOut }' });
    });
    it('round-trips an eav write node and strips template-resolution markers from config', () => {
      useNodeStore.getState().addNode('stepflow:data:eav', { x: 0, y: 0 });
      const node = useNodeStore.getState().nodes[0];
      node.data.configuration = { operation: 'write', entityType: 'refund_decision' };

      const exported = FlowService.exportFlow();
      expect(exported.states[node.id].parameters).toMatchObject({ 'values.$': '$' });

      useNodeStore.setState({ nodes: [] });
      useEdgeStore.setState({ edges: [] });
      FlowService.importFlow(exported);

      const imported = useNodeStore.getState().nodes[0];
      expect(imported.data?.schemaId).toBe('stepflow:data:eav');
      expect(imported.data?.configuration).toEqual({ operation: 'write', entityType: 'refund_decision' });
    });

    it('round-trips terminal end states through export and import', () => {
      useNodeStore.getState().addNode('stepflow:terminal:start', { x: 0, y: 0 });
      useNodeStore.getState().addNode('stepflow:terminal:end', { x: 0, y: 150 });
      const [start, end] = useNodeStore.getState().nodes;
      useEdgeStore.getState().addEdge({ id: 'edge-rt', source: start.id, target: end.id, type: 'step-edge' });

      const exported = FlowService.exportFlow();
      expect(exported.states[end.id]).toMatchObject({ type: 'Pass', end: true });

      useNodeStore.setState({ nodes: [] });
      useEdgeStore.setState({ edges: [] });
      FlowService.importFlow(exported);

      const nodes = useNodeStore.getState().nodes;
      // Start maps back to a plain pass (pre-existing limitation); end must be exact.
      expect(nodes.map((n) => n.data?.schemaId)).toEqual(['stepflow:utility:pass', 'stepflow:terminal:end']);
    });
    it('imports an ASL Choice state into a choice node with branch handles and condition', () => {
      FlowService.importFlow({
        startAt: 'Check',
        states: {
          Check: { type: 'Task', resource: 'https://example.com/health', parameters: { __handler: 'http', method: 'GET', includeStatus: true }, next: 'Healthy?' },
          'Healthy?': { type: 'Choice', choices: [{ expression: '$.status < 400', next: 'ReportOK' }], default: 'ReportIssue' },
          ReportOK: { type: 'Task', resource: 'transform://jsonata', parameters: { expression: '{ healthy: true, status: $.status }' } },
          ReportIssue: { type: 'Task', resource: 'transform://jsonata', parameters: { expression: '{ healthy: false, status: $.status }' } },
        },
      });

      const nodes = useNodeStore.getState().nodes;
      expect(nodes).toHaveLength(4);
      const choiceNode = nodes.find((n) => n.data?.schemaId === 'stepflow:flow:choice');
      expect(choiceNode).toBeDefined();
      expect(choiceNode!.data?.configuration).toMatchObject({ condition: '$.status < 400' });

      const httpNode = nodes.find((n) => n.data?.schemaId === 'stepflow:api:http')!;
      expect(httpNode.data?.configuration).toMatchObject({ method: 'GET', url: 'https://example.com/health', includeStatus: true });

      const okNode = nodes.find((n) => n.data?.label === 'ReportOK')!;
      const issueNode = nodes.find((n) => n.data?.label === 'ReportIssue')!;
      const edges = useEdgeStore.getState().edges;
      expect(edges.find((e) => e.source === choiceNode!.id && e.target === okNode.id)?.sourceHandle).toBe('output_true');
      expect(edges.find((e) => e.source === choiceNode!.id && e.target === issueNode.id)?.sourceHandle).toBe('output_false');
    });

    it('synthesizes a JSONata condition for structured ASL choice rules on import', () => {
      FlowService.importFlow({
        startAt: 'Branch',
        states: {
          Branch: { type: 'Choice', choices: [{ Variable: '$.city', StringEquals: 'Wellington', next: 'A' }], default: 'B' },
          A: { type: 'Succeed' },
          B: { type: 'Succeed' },
        },
      });

      const choiceNode = useNodeStore.getState().nodes.find((n) => n.data?.schemaId === 'stepflow:flow:choice');
      expect(choiceNode!.data?.configuration).toMatchObject({ condition: '$.city = "Wellington"' });
    });
  });
});
