import { useCallback } from 'react';
import { useExecutionStore } from '@stores/useExecutionStore';

/**
 * Hook to track and visualize execution progress on nodes.
 */
export function useExecutionProgress() {
  const startExecution = useExecutionStore((s) => s.startExecution);
  const stopExecution = useExecutionStore((s) => s.stopExecution);
  const status = useExecutionStore((s) => s.status);
  const currentNodeId = useExecutionStore((s) => s.currentNodeId);
  const completedNodes = useExecutionStore((s) => s.completedNodes);
  const failedNodes = useExecutionStore((s) => s.failedNodes);

  const getNodeStatus = useCallback(
    (nodeId: string): 'idle' | 'running' | 'completed' | 'failed' => {
      if (failedNodes.has(nodeId)) return 'failed';
      if (completedNodes.has(nodeId)) return 'completed';
      if (currentNodeId === nodeId) return 'running';
      return 'idle';
    },
    [currentNodeId, completedNodes, failedNodes]
  );

  return {
    startExecution,
    stopExecution,
    status,
    currentNodeId,
    completedNodes,
    failedNodes,
    getNodeStatus,
  };
}
