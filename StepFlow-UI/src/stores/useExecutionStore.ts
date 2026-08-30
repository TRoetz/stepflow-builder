import { create } from 'zustand';

export type ExecutionStatus = 'idle' | 'running' | 'paused' | 'completed' | 'failed';
export type ExecutionMode = 'simulated' | 'backend';

export interface StepExecutionLog {
  nodeId: string;
  nodeName: string;
  status: 'running' | 'completed' | 'failed';
  input: any;
  output: any;
  error?: string;
  passthrough?: boolean; // true when a Pass Through node forwarded its input unmodified (Enable Logging on)
  startTime: number;
  endTime?: number;
}

interface ExecutionState {
  status: ExecutionStatus;
  executionMode: ExecutionMode;
  currentNodeId: string | null;
  completedNodes: Set<string>;
  failedNodes: Map<string, string>; // nodeId -> error message
  logs: Record<string, StepExecutionLog>; // nodeId -> log
  startTime: number | null;
  endTime: number | null;

  // Actions
  setExecutionMode: (mode: ExecutionMode) => void;
  setNodeLog: (nodeId: string, log: Partial<StepExecutionLog>) => void;
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
  executionMode: 'simulated',
  currentNodeId: null,
  completedNodes: new Set<string>(),
  failedNodes: new Map<string, string>(),
  logs: {},
  startTime: null,
  endTime: null,

  setNodeLog: (nodeId, log) => {
    set((state) => {
      const existing = state.logs[nodeId] || {
        nodeId,
        nodeName: '',
        status: 'running',
        input: null,
        output: null,
        startTime: Date.now(),
      };
      return {
        logs: {
          ...state.logs,
          [nodeId]: {
            ...existing,
            ...log,
            endTime: log.status === 'running' ? undefined : Date.now(),
          } as StepExecutionLog,
        },
      };
    });
  },

  setExecutionMode: (mode) => set({ executionMode: mode }),

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
