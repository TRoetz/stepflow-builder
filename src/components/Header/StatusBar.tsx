import { CheckCircle, Activity, AlertCircle, Loader2 } from 'lucide-react';

interface StatusBarProps {
  isRunning: boolean;
  nodeCount: number;
  edgeCount: number;
  zoom: number;
  executionStatus?: 'idle' | 'running' | 'paused' | 'completed' | 'failed';
}

const statusConfig: Record<string, { label: string; color: string; icon: React.ReactNode }> = {
  idle: { label: 'Idle', color: 'text-green-400', icon: <CheckCircle className="w-3.5 h-3.5" /> },
  running: { label: 'Running', color: 'text-amber-400', icon: <Loader2 className="w-3.5 h-3.5 animate-spin" /> },
  paused: { label: 'Paused', color: 'text-blue-400', icon: <Activity className="w-3.5 h-3.5" /> },
  completed: { label: 'Completed', color: 'text-green-400', icon: <CheckCircle className="w-3.5 h-3.5" /> },
  failed: { label: 'Failed', color: 'text-red-400', icon: <AlertCircle className="w-3.5 h-3.5" /> },
};

export function StatusBar({ nodeCount, edgeCount, zoom, executionStatus = 'idle' }: StatusBarProps) {
  const status = statusConfig[executionStatus] || statusConfig.idle;

  return (
    <footer className="app-statusbar">
      <div className="flex items-center gap-4">
        <span className="flex items-center gap-1.5">
          <Activity className="w-3.5 h-3.5" />
          Execution: <span className={status.color}>{status.label}</span>
        </span>
        <span className="flex items-center gap-1.5">
          <CheckCircle className="w-3.5 h-3.5 text-green-500" />
          Valid
        </span>
      </div>
      <div className="flex items-center gap-4">
        <span>Nodes: {nodeCount}</span>
        <span>Edges: {edgeCount}</span>
        <span>Zoom: {Math.round(zoom * 100)}%</span>
      </div>
    </footer>
  );
}
