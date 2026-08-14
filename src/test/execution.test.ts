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
