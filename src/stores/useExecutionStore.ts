import { create } from 'zustand';

export type ExecutionStatus = 'idle' | 'running' | 'paused' | 'completed' | 'failed';

interface ExecutionState {
  status: ExecutionStatus;
  currentNodeId: string | null;
  completedNodes: Set<string>;
  failedNodes: Map<string, string>; // nodeId -> error message
  startTime: number | null;
  endTime: number | null;

  // Actions
  startExecution: () => void;
  stopExecution: () => void;
  pauseExecution: () => void;
  resumeExecution: () => void;
  setNodeRunning: (nodeId: string) => void;
  setNodeCompleted: (nodeId: string) => void;
  setNodeFailed: (nodeId: string, error: string) => void;
  resetExecution: () => void;
}

export const useExecutionStore = create<ExecutionState>((set) => ({
  status: 'idle',
  currentNodeId: null,
  completedNodes: new Set<string>(),
  failedNodes: new Map<string, string>(),
  startTime: null,
  endTime: null,

  startExecution: () => {
    set({
      status: 'running',
      currentNodeId: null,
      completedNodes: new Set<string>(),
      failedNodes: new Map<string, string>(),
      startTime: Date.now(),
      endTime: null,
    });
  },

  stopExecution: () => {
    set({
      status: 'idle',
      currentNodeId: null,
      endTime: Date.now(),
    });
  },

  pauseExecution: () => {
    set({
      status: 'paused',
      currentNodeId: null,
    });
  },

  resumeExecution: () => {
    set({
      status: 'running',
    });
  },

  setNodeRunning: (nodeId: string) => {
    set({
      currentNodeId: nodeId,
    });
  },

  setNodeCompleted: (nodeId: string) => {
    set((state) => {
      const completed = new Set(state.completedNodes);
      completed.add(nodeId);
      return {
        completedNodes: completed,
        currentNodeId: null,
      };
    });
  },

  setNodeFailed: (nodeId: string, error: string) => {
    set((state) => {
      const failed = new Map(state.failedNodes);
      failed.set(nodeId, error);
      return {
        status: 'failed' as ExecutionStatus,
        failedNodes: failed,
        currentNodeId: null,
        endTime: Date.now(),
      };
    });
  },

  resetExecution: () => {
    set({
      status: 'idle',
      currentNodeId: null,
      completedNodes: new Set<string>(),
      failedNodes: new Map<string, string>(),
      startTime: null,
      endTime: null,
    });
  },
}));
