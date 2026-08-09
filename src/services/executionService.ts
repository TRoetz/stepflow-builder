import { useExecutionStore } from '@stores/useExecutionStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { StepNode } from '@stores/useNodeStore';
import { StepEdge } from '@stores/useEdgeStore';

/**
 * Service for flow execution.
 * Simulates execution by traversing nodes in order.
 * In production, this would call the .NET backend API.
 */
export const ExecutionService = {
  /**
   * Start flow execution.
   */
  async startExecution(): Promise<void> {
    const executionStore = useExecutionStore.getState();
    executionStore.startExecution();

    const nodes = useNodeStore.getState().nodes;
    const edges = useEdgeStore.getState().edges;

    if (nodes.length === 0) {
      executionStore.setNodeFailed('canvas', 'No nodes in flow');
      return;
    }

    // Find start node (node with no incoming edges)
    const targetNodeIds = new Set(edges.map((e) => e.target));
    const startNode = nodes.find((n) => !targetNodeIds.has(n.id)) || nodes[0];

    try {
      const visited = new Set<string>();
      await traverseNode(startNode, nodes, edges, executionStore, visited);
      useExecutionStore.setState({ status: 'completed', endTime: Date.now() });
    } catch (error) {
      executionStore.setNodeFailed(startNode.id, String(error));
    }
  },

  /**
   * Stop execution.
   */
  async stopExecution(): Promise<void> {
    useExecutionStore.getState().stopExecution();
  },

  /**
   * Test a sub-flow connection.
   */
  async testSubFlow(flowId: string): Promise<{ success: boolean; message: string }> {
    try {
      console.log(`Testing sub-flow: ${flowId}`);
      return { success: true, message: `Sub-flow ${flowId} is reachable` };
    } catch (error) {
      return { success: false, message: String(error) };
    }
  },
};

// ── Execution Traversal ──

async function traverseNode(
  node: StepNode,
  nodes: StepNode[],
  edges: StepEdge[],
  executionStore: ReturnType<typeof useExecutionStore.getState>,
  visited: Set<string>,
  delay: number = 300
): Promise<void> {
  if (visited.has(node.id)) return;
  visited.add(node.id);

  executionStore.setNodeRunning(node.id);

  // Simulate processing delay
  await sleep(delay);

  executionStore.setNodeCompleted(node.id);

  // Find next nodes (outgoing edges)
  const nextEdges = edges.filter((e) => e.source === node.id);
  for (const edge of nextEdges) {
    const nextNode = nodes.find((n) => n.id === edge.target);
    if (nextNode) {
      await traverseNode(nextNode, nodes, edges, executionStore, visited, delay);
    }
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
