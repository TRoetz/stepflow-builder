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
    useNodeStore.setState({ nodes: [], selectedNodeId: null });
    useEdgeStore.setState({ edges: [], selectedEdgeId: null });
  });

  it('should start with empty history', () => {
    expect(useUndoRedoStore.getState().canUndo).toBe(false);
    expect(useUndoRedoStore.getState().canRedo).toBe(false);
  });

  it('undo() does nothing when history is empty', () => {
    useUndoRedoStore.getState().undo();
    expect(useUndoRedoStore.getState().canUndo).toBe(false);
    expect(useUndoRedoStore.getState().canRedo).toBe(false);
  });

  it('undo() restores the canvas from before a node was added', () => {
    useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 });
    expect(useNodeStore.getState().nodes).toHaveLength(1);
    expect(useUndoRedoStore.getState().canUndo).toBe(true);

    useUndoRedoStore.getState().undo();
    expect(useNodeStore.getState().nodes).toHaveLength(0);
    expect(useUndoRedoStore.getState().canRedo).toBe(true);
  });

  it('undo() brings back a removed node together with its cascaded edges', () => {
    const a = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 })!;
    const b = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 100, y: 0 })!;
    useEdgeStore.getState().addEdge({ id: 'e-ab', source: a.id, target: b.id });
    useUndoRedoStore.getState().clearHistory();

    useNodeStore.getState().removeNode(a.id);
    expect(useNodeStore.getState().nodes).toHaveLength(1);
    expect(useEdgeStore.getState().edges).toHaveLength(0);

    useUndoRedoStore.getState().undo();
    expect(useNodeStore.getState().nodes).toHaveLength(2);
    expect(useEdgeStore.getState().edges).toHaveLength(1);
  });

  it('redo() re-applies an undone removal', () => {
    const a = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 })!;
    useUndoRedoStore.getState().clearHistory();

    useNodeStore.getState().removeNode(a.id);
    useUndoRedoStore.getState().undo();
    expect(useNodeStore.getState().nodes).toHaveLength(1);

    useUndoRedoStore.getState().redo();
    expect(useNodeStore.getState().nodes).toHaveLength(0);
    expect(useUndoRedoStore.getState().canUndo).toBe(true);
  });

  it('a new action clears the redo branch', () => {
    const a = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 })!;
    useUndoRedoStore.getState().clearHistory();
    useNodeStore.getState().removeNode(a.id);
    useUndoRedoStore.getState().undo();
    expect(useUndoRedoStore.getState().canRedo).toBe(true);

    useEdgeStore.getState().addEdge({ id: 'e-x', source: 'a', target: 'b' });
    expect(useUndoRedoStore.getState().canRedo).toBe(false);
  });

  it('snapshots are deep copies: mutating a live node cannot rewrite history', () => {
    const node = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 })!;
    useUndoRedoStore.getState().clearHistory();
    useUndoRedoStore.getState().pushSnapshot();
    const serialized = JSON.stringify(useUndoRedoStore.getState().undoStack);
    expect(useUndoRedoStore.getState().undoStack[0].nodes).toHaveLength(1);

    // Write straight through to the live node object — history must not see it.
    node.data.description = 'aliasing attempt';
    expect(JSON.stringify(useUndoRedoStore.getState().undoStack)).toBe(serialized);
  });

  it('consecutive edits to one field undo as a single step', () => {
    const node = useNodeStore.getState().addNode('stepflow:utility:pass', { x: 0, y: 0 })!;
    const originalDescription = node.data.description;
    useUndoRedoStore.getState().clearHistory();

    // Three rapid keystrokes into the same field — should collapse.
    useNodeStore.getState().updateNodeData(node.id, { description: 'a' });
    useNodeStore.getState().updateNodeData(node.id, { description: 'ab' });
    useNodeStore.getState().updateNodeData(node.id, { description: 'abc' });
    expect(useUndoRedoStore.getState().undoStack).toHaveLength(1);

    useUndoRedoStore.getState().undo();
    expect(useNodeStore.getState().nodes[0].data.description).toBe(originalDescription);
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
