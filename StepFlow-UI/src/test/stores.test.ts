import { describe, it, expect, beforeEach } from 'vitest';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useExecutionStore } from '@stores/useExecutionStore';
import { useUndoRedoStore } from '@stores/useUndoRedoStore';
import { useSettingsStore } from '@stores/useSettingsStore';

describe('Node Store', () => {
  beforeEach(() => {
    useNodeStore.setState({ nodes: [], selectedNodeId: null });
  });

  it('should start with empty nodes', () => {
    expect(useNodeStore.getState().nodes).toHaveLength(0);
  });

  it('should add a node', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 100, y: 100 });
    const { nodes } = useNodeStore.getState();
    expect(nodes).toHaveLength(1);
    expect(nodes[0].data.schemaId).toBe('stepflow:utility:pass');
    expect(nodes[0].position).toEqual({ x: 100, y: 100 });
  });

  it('should update node data', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().updateNodeData(nodeId, {
      configuration: { test: 'value' },
      description: 'Test node',
    });
    const node = useNodeStore.getState().nodes.find((n) => n.id === nodeId);
    expect(node?.data.configuration).toEqual({ test: 'value' });
    expect(node?.data.description).toBe('Test node');
  });

  it('should remove a node', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().removeNode(nodeId);
    expect(useNodeStore.getState().nodes).toHaveLength(0);
  });

  it('should duplicate a node', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().duplicateNode(nodeId);
    expect(useNodeStore.getState().nodes).toHaveLength(2);
  });

  it('should handle onNodesChange position', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().onNodesChange([{
      type: 'position',
      id: nodeId,
      position: { x: 200, y: 200 },
    }]);
    const node = useNodeStore.getState().nodes.find((n) => n.id === nodeId);
    expect(node?.position).toEqual({ x: 200, y: 200 });
  });

  it('should handle onNodesChange remove', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().onNodesChange([{
      type: 'remove',
      id: nodeId,
    }]);
    expect(useNodeStore.getState().nodes).toHaveLength(0);
  });

  it('should set selected node', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().setSelectedNode(nodeId);
    expect(useNodeStore.getState().selectedNodeId).toBe(nodeId);
  });

  it('should get selected node', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    const nodeId = useNodeStore.getState().nodes[0].id;
    useNodeStore.getState().setSelectedNode(nodeId);
    const selected = useNodeStore.getState().getSelectedNode();
    expect(selected?.id).toBe(nodeId);
  });
});

describe('Edge Store', () => {
  beforeEach(() => {
    useEdgeStore.setState({ edges: [] });
  });

  it('should start with empty edges', () => {
    expect(useEdgeStore.getState().edges).toHaveLength(0);
  });

  it('should add an edge', () => {
    useEdgeStore.getState().addEdge({
      id: 'edge-1',
      source: 'node-1',
      target: 'node-2',
    });
    expect(useEdgeStore.getState().edges).toHaveLength(1);
    expect(useEdgeStore.getState().edges[0].source).toBe('node-1');
  });

  it('should remove an edge', () => {
    useEdgeStore.getState().addEdge({
      id: 'edge-1',
      source: 'node-1',
      target: 'node-2',
    });
    useEdgeStore.getState().removeEdge('edge-1');
    expect(useEdgeStore.getState().edges).toHaveLength(0);
  });

  it('should remove edges by node ID', () => {
    useEdgeStore.getState().addEdge({
      id: 'edge-1',
      source: 'node-1',
      target: 'node-2',
    });
    useEdgeStore.getState().addEdge({
      id: 'edge-2',
      source: 'node-2',
      target: 'node-3',
    });
    useEdgeStore.getState().removeEdgesByNodeId('node-2');
    expect(useEdgeStore.getState().edges).toHaveLength(0);
  });

  it('should clear all edges', () => {
    useEdgeStore.getState().addEdge({ id: 'e1', source: 'a', target: 'b' });
    useEdgeStore.getState().addEdge({ id: 'e2', source: 'b', target: 'c' });
    useEdgeStore.getState().clearEdges();
    expect(useEdgeStore.getState().edges).toHaveLength(0);
  });

  it('should handle onEdgesChange remove', () => {
    useEdgeStore.getState().addEdge({
      id: 'edge-1',
      source: 'node-1',
      target: 'node-2',
    });
    useEdgeStore.getState().onEdgesChange([{
      type: 'remove',
      id: 'edge-1',
    }]);
    expect(useEdgeStore.getState().edges).toHaveLength(0);
  });
});

describe('Execution Store', () => {
  beforeEach(() => {
    useExecutionStore.getState().resetExecution();
  });

  it('should start in idle state', () => {
    expect(useExecutionStore.getState().status).toBe('idle');
  });

  it('should start execution', () => {
    useExecutionStore.getState().startExecution();
    expect(useExecutionStore.getState().status).toBe('running');
    expect(useExecutionStore.getState().startTime).toBeDefined();
  });

  it('should set node running', () => {
    useExecutionStore.getState().setNodeRunning('node-1');
    expect(useExecutionStore.getState().currentNodeId).toBe('node-1');
  });

  it('should set node completed', () => {
    useExecutionStore.getState().setNodeCompleted('node-1');
    expect(useExecutionStore.getState().completedNodes.has('node-1')).toBe(true);
  });

  it('should set node failed', () => {
    useExecutionStore.getState().setNodeFailed('node-1', 'Test error');
    expect(useExecutionStore.getState().failedNodes.has('node-1')).toBe(true);
    expect(useExecutionStore.getState().status).toBe('failed');
  });

  it('should pause execution', () => {
    useExecutionStore.getState().startExecution();
    useExecutionStore.getState().pauseExecution();
    expect(useExecutionStore.getState().status).toBe('paused');
  });

  it('should resume execution', () => {
    useExecutionStore.getState().pauseExecution();
    useExecutionStore.getState().resumeExecution();
    expect(useExecutionStore.getState().status).toBe('running');
  });

  it('should stop execution', () => {
    useExecutionStore.getState().startExecution();
    useExecutionStore.getState().stopExecution();
    expect(useExecutionStore.getState().status).toBe('idle');
  });

  it('should reset execution', () => {
    useExecutionStore.getState().startExecution();
    useExecutionStore.getState().setNodeCompleted('node-1');
    useExecutionStore.getState().resetExecution();
    expect(useExecutionStore.getState().status).toBe('idle');
    expect(useExecutionStore.getState().completedNodes.size).toBe(0);
    expect(useExecutionStore.getState().startTime).toBeNull();
  });
});

describe('Undo/Redo Store', () => {
  beforeEach(() => {
    useUndoRedoStore.getState().clearHistory();
  });

  it('should start with empty history', () => {
    expect(useUndoRedoStore.getState().canUndo).toBe(false);
    expect(useUndoRedoStore.getState().canRedo).toBe(false);
  });

  it('should push state', () => {
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().pushState([], []);
    expect(useUndoRedoStore.getState().canUndo).toBe(true);
  });

  it('should undo', () => {
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().undo();
    expect(useUndoRedoStore.getState().canRedo).toBe(true);
  });

  it('should redo', () => {
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().undo();
    useUndoRedoStore.getState().redo();
    expect(useUndoRedoStore.getState().canUndo).toBe(true);
  });

  it('should clear history', () => {
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().pushState([], []);
    useUndoRedoStore.getState().clearHistory();
    expect(useUndoRedoStore.getState().canUndo).toBe(false);
    expect(useUndoRedoStore.getState().canRedo).toBe(false);
  });
});

describe('Settings Store', () => {
  it('should have default settings', () => {
    const settings = useSettingsStore.getState();
    expect(settings.theme).toBe('dark');
    expect(settings.snapToGrid).toBe(true);
    expect(settings.snapGridSize).toBe(16);
  });

  it('should update settings', () => {
    useSettingsStore.setState({ theme: 'light', snapToGrid: false });
    const settings = useSettingsStore.getState();
    expect(settings.theme).toBe('light');
    expect(settings.snapToGrid).toBe(false);
  });
});
