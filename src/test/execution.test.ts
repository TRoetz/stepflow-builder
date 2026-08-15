import { describe, it, expect, beforeEach, vi } from 'vitest';
import { useExecutionStore } from '@stores/useExecutionStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { FlowService } from '@services/flowService';
import { ExecutionService } from '@services/executionService';

describe('Execution Mode & Store', () => {
  beforeEach(() => {
    useExecutionStore.getState().resetExecution();
  });

  it('should default to simulated execution mode', () => {
    const store = useExecutionStore.getState();
    expect(store.executionMode).toBe('simulated');
    expect(store.status).toBe('idle');
  });

  it('should allow toggling execution mode', () => {
    const store = useExecutionStore.getState();
    store.setExecutionMode('backend');
    expect(useExecutionStore.getState().executionMode).toBe('backend');

    store.setExecutionMode('simulated');
    expect(useExecutionStore.getState().executionMode).toBe('simulated');
  });
});

describe('ASL State Type Translations', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });
  });

  it('should translate canvas nodes to standard ASL StateType values', () => {
    // 1. Add start node (should translate to 'Pass' in ASL)
    useNodeStore.getState().addNode('stepflow:terminal:start');
    
    // 2. Add HTTP node (should translate to 'Task' in ASL)
    useNodeStore.getState().addNode('stepflow:api:http');

    // 3. Add Script node (should translate to 'Task' in ASL)
    useNodeStore.getState().addNode('stepflow:transform:script');

    // 4. Add wait node (should translate to 'Wait' in ASL)
    useNodeStore.getState().addNode('stepflow:utility:wait');

    const exported = FlowService.exportFlow();
    const states = Object.values(exported.states);

    // Verify correct translations
    const types = states.map(s => s.type);
    expect(types).toContain('Pass');
    expect(types).toContain('Task');
    expect(types).toContain('Wait');
    
    // None should contain original categories like 'terminal' or 'api'
    expect(types).not.toContain('terminal');
    expect(types).not.toContain('api');
    expect(types).not.toContain('transform');
  });
});

describe('Browser Execution Engine (Simulated)', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });
    useExecutionStore.getState().resetExecution();
    vi.restoreAllMocks();
  });

  it('should traverse and execute nodes in sequence', async () => {
    // Set up a simple 2-node flow
    useNodeStore.getState().addNode('stepflow:terminal:start');
    const nodes = useNodeStore.getState().nodes;
    const startNode = nodes[0];

    useNodeStore.getState().addNode('stepflow:terminal:end');
    const endNode = useNodeStore.getState().nodes[1];

    useEdgeStore.getState().addEdge({
      id: 'edge-1',
      source: startNode.id,
      target: endNode.id,
      type: 'step-edge',
    });

    // Run execution with a fast delay
    const startPromise = ExecutionService.startExecution();
    
    // Verify store status goes to running
    expect(useExecutionStore.getState().status).toBe('running');

    await startPromise;

    // Verify it completes successfully
    expect(useExecutionStore.getState().status).toBe('completed');
    expect(useExecutionStore.getState().completedNodes.size).toBe(2);
  });
});

describe('Pass Through state (stepflow:utility:pass)', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });
    useExecutionStore.getState().resetExecution();
  });

  const buildPassFlow = (enableLogging: boolean) => {
    useNodeStore.getState().addNode('stepflow:terminal:start');
    useNodeStore.getState().addNode('stepflow:transform:script');
    useNodeStore.getState().addNode('stepflow:utility:pass');
    useNodeStore.getState().addNode('stepflow:terminal:end');
    const [start, script, pass, end] = useNodeStore.getState().nodes;

    // Script node produces a known payload that the Pass Through node must forward.
    useNodeStore.getState().updateNodeData(script.id, {
      configuration: { script: 'return { orderId: 42, paid: true };', language: 'javascript' },
    });
    useNodeStore.getState().updateNodeData(pass.id, {
      configuration: { enableLogging },
    });

    const addEdge = useEdgeStore.getState().addEdge;
    addEdge({ id: 'e1', source: start.id, target: script.id, type: 'step-edge' });
    addEdge({ id: 'e2', source: script.id, target: pass.id, type: 'step-edge' });
    addEdge({ id: 'e3', source: pass.id, target: end.id, type: 'step-edge' });

    return { start, script, pass, end };
  };

  it('forwards upstream payload to its output and downstream nodes unmodified', async () => {
    const { pass, end } = buildPassFlow(true);
    await ExecutionService.startExecution();

    const logs = useExecutionStore.getState().logs;
    expect(logs[pass.id].status).toBe('completed');
    // The exact payload produced upstream arrives at the Pass node...
    expect(logs[pass.id].input).toEqual({ orderId: 42, paid: true });
    // ...and exits unchanged instead of being replaced by {}.
    expect(logs[pass.id].output).toEqual({ orderId: 42, paid: true });
    // Downstream node receives the same payload (previously it got {}). 
    expect(logs[end.id].input).toEqual({ orderId: 42, paid: true });
  });

  it('marks the log as passthrough when Enable Logging is on', async () => {
    const { pass } = buildPassFlow(true);
    await ExecutionService.startExecution();
    expect(useExecutionStore.getState().logs[pass.id].passthrough).toBe(true);
  });

  it('still passes data through with logging off (no passthrough marker)', async () => {
    const { pass } = buildPassFlow(false);
    await ExecutionService.startExecution();

    const log = useExecutionStore.getState().logs[pass.id];
    expect(log.status).toBe('completed');
    expect(log.output).toEqual({ orderId: 42, paid: true });
    expect(log.passthrough).toBeFalsy();
  });

  it('single-node test treats pass as an identity transform', async () => {
    useNodeStore.getState().addNode('stepflow:utility:pass');
    const [pass] = useNodeStore.getState().nodes;
    const output = await ExecutionService.testSingleNode(pass, { a: 1 });
    expect(output).toEqual({ a: 1 });
  });
});
