import { describe, it, expect, beforeEach, vi } from 'vitest';
import { useExecutionStore } from '@stores/useExecutionStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { FlowService } from '@services/flowService';
import { ExecutionService } from '@services/executionService';

import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import * as AiAssistantStore from '@stores/useAiAssistantStore';

// Mock only the outbound API call; everything else (config resolution,
// decision parsing, simulation fallback) runs for real.
vi.mock('@stores/useAiAssistantStore', async (importOriginal) => {
  const actual = await importOriginal<typeof AiAssistantStore>();
  return { ...actual, callAiApi: vi.fn() };
});

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

describe('Local AI Step Execution (saved local model config)', () => {
  const savedLocalConfig = {
    provider: 'ollama',
    baseUrl: 'http://127.0.0.1:11434',
    apiKey: '',
    defaultModel: 'llama3.2',
    temperature: 0.2,
    maxTokens: 256,
    topP: 0.9,
  } as const;

  const configureSavedLocal = (nodeId: string, overrides: Record<string, unknown> = {}) => {
    useNodeStore.getState().updateNodeData(nodeId, {
      configuration: {
        llmService: 'ollama',
        modelSource: 'saved',
        prompt: 'Analyze the input.',
        outputFormat: 'json',
        ...overrides,
      },
    });
  };

  // updateNodeData replaces the node object; always re-read the node from the
  // store after configuring so execution sees the fresh data.
  const firstNode = () => {
    const [node] = useNodeStore.getState().nodes;
    if (!node) throw new Error('expected a node in store');
    return node;
  };

  beforeEach(() => {
    useNodeStore.setState({ nodes: [] });
    useEdgeStore.setState({ edges: [] });
    useExecutionStore.getState().resetExecution();
    useAiModelConfigStore.setState(savedLocalConfig);
    vi.mocked(AiAssistantStore.callAiApi).mockReset();
  });

  it('decision node with saved local config calls the saved endpoint and parses the JSON decision', async () => {
    useNodeStore.getState().addNode('stepflow:ai:decision');
    configureSavedLocal(firstNode().id, { prompt: 'Should we approve this order?' });

    vi.mocked(AiAssistantStore.callAiApi).mockResolvedValueOnce('{"decision": true}');

    const output = await ExecutionService.testSingleNode(firstNode(), { orderId: 7 });

    expect(vi.mocked(AiAssistantStore.callAiApi)).toHaveBeenCalledTimes(1);
    const [callConfig, systemMessage, userMessage] = vi.mocked(AiAssistantStore.callAiApi).mock.calls[0];
    expect(callConfig).toMatchObject({
      provider: 'ollama',
      baseUrl: 'http://127.0.0.1:11434',
      defaultModel: 'llama3.2',
      temperature: 0.2,
    });
    expect(systemMessage).toContain('Should we approve this order?');
    expect(userMessage).toContain('{"orderId":7}');

    expect(output).toMatchObject({
      Decision: true,
      raw: '{"decision": true}',
      metadata: { provider: 'local', service: 'ollama', model: 'llama3.2', baseUrl: 'http://127.0.0.1:11434' },
    });
  });

  it('text gen node with saved local config returns the generated text and honors node temperature', async () => {
    useNodeStore.getState().addNode('stepflow:ai:text');
    configureSavedLocal(firstNode().id, { llmService: 'lmStudio', temperature: 0.5 });

    vi.mocked(AiAssistantStore.callAiApi).mockResolvedValueOnce('The order is ready to ship.');

    const output = await ExecutionService.testSingleNode(firstNode(), { orderId: 7 });

    // Node temperature (0.5) overrides the saved config temperature (0.2).
    expect(vi.mocked(AiAssistantStore.callAiApi).mock.calls[0][0]).toMatchObject({ provider: 'lmStudio', temperature: 0.5 });
    expect(output).toMatchObject({
      GeneratedText: 'The order is ready to ship.',
      metadata: { provider: 'local', service: 'lmStudio', model: 'llama3.2' },
    });
  });

  it('failed local model call fails the step with the API error', async () => {
    useNodeStore.getState().addNode('stepflow:ai:decision');
    const nodeId = firstNode().id;
    configureSavedLocal(nodeId);

    vi.mocked(AiAssistantStore.callAiApi).mockRejectedValueOnce(new Error('connect ECONNREFUSED 127.0.0.1:11434'));

    await expect(ExecutionService.testSingleNode(firstNode(), {})).rejects.toThrow(
      'Local AI model call failed: connect ECONNREFUSED 127.0.0.1:11434'
    );
    expect(useExecutionStore.getState().logs[nodeId].status).toBe('failed');
  });

  it('non-saved configs (cloud service, local without saved source) stay simulated without a real call', async () => {
    const a = useNodeStore.getState().addNode('stepflow:ai:decision');
    const b = useNodeStore.getState().addNode('stepflow:ai:decision');
    if (!a || !b) throw new Error('expected two nodes');
    useNodeStore.getState().updateNodeData(a.id, {
      configuration: { llmService: 'azureOpenAI', modelSource: 'node', model: 'gpt-4-turbo', prompt: 'p' },
    });
    useNodeStore.getState().updateNodeData(b.id, {
      configuration: { llmService: 'ollama', modelSource: 'node', model: 'llama3.2', prompt: 'p' },
    });

    const [cloudNode, localNode] = useNodeStore.getState().nodes;
    const cloudOut = await ExecutionService.testSingleNode(cloudNode, {});
    const localOut = await ExecutionService.testSingleNode(localNode, {});

    expect(vi.mocked(AiAssistantStore.callAiApi)).not.toHaveBeenCalled();
    expect(typeof cloudOut.Decision).toBe('boolean');
    expect(cloudOut.metadata).toMatchObject({ provider: 'simulated', service: 'azureOpenAI' });
    expect(localOut.metadata).toMatchObject({ provider: 'simulated', service: 'ollama' });
  });

  it('flow execution performs the real local call and forwards the result downstream', async () => {
    useNodeStore.getState().addNode('stepflow:terminal:start');
    useNodeStore.getState().addNode('stepflow:ai:decision');
    useNodeStore.getState().addNode('stepflow:terminal:end');
    const [start, decision, end] = useNodeStore.getState().nodes;
    configureSavedLocal(decision.id);

    const addEdge = useEdgeStore.getState().addEdge;
    addEdge({ id: 'e1', source: start.id, target: decision.id, type: 'step-edge' });
    addEdge({ id: 'e2', source: decision.id, target: end.id, type: 'step-edge' });

    vi.mocked(AiAssistantStore.callAiApi).mockResolvedValueOnce('{"decision": false}');

    await ExecutionService.startExecution();

    const logs = useExecutionStore.getState().logs;
    expect(useExecutionStore.getState().status).toBe('completed');
    expect(logs[decision.id].output).toMatchObject({ Decision: false, metadata: { provider: 'local' } });
    expect(logs[end.id].input).toEqual(logs[decision.id].output);
  });
});
