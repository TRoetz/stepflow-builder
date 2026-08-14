import { CheckCircle, Activity, AlertCircle, Loader2, Terminal } from 'lucide-react';

interface StatusBarProps {
  isRunning: boolean;
  nodeCount: number;
  edgeCount: number;
  zoom: number;
  executionStatus?: 'idle' | 'running' | 'paused' | 'completed' | 'failed';
  showLogs?: boolean;
  onToggleLogs?: () => void;
  logCount?: number;
}

const statusConfig: Record<string, { label: string; color: string; icon: React.ReactNode }> = {
  idle: { label: 'Idle', color: 'text-green-400', icon: <CheckCircle className="w-3.5 h-3.5" /> },
  running: { label: 'Running', color: 'text-amber-400', icon: <Loader2 className="w-3.5 h-3.5 animate-spin" /> },
  paused: { label: 'Paused', color: 'text-blue-400', icon: <Activity className="w-3.5 h-3.5" /> },
  completed: { label: 'Completed', color: 'text-green-400', icon: <CheckCircle className="w-3.5 h-3.5" /> },
  failed: { label: 'Failed', color: 'text-red-400', icon: <AlertCircle className="w-3.5 h-3.5" /> },
};

export function StatusBar({ nodeCount, edgeCount, zoom, executionStatus = 'idle', showLogs = false, onToggleLogs, logCount = 0 }: StatusBarProps) {
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

        {onToggleLogs && (
          <button
            onClick={onToggleLogs}
            className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[10px] font-semibold border transition-all ${
              showLogs
                ? 'bg-indigo-600 border-indigo-500 text-white font-bold'
                : 'bg-gray-800/80 hover:bg-gray-700/80 border-gray-700/60 text-gray-300'
            }`}
          >
            <Terminal className="w-3.5 h-3.5" />
            <span>Execution Logs</span>
            {logCount > 0 && (
              <span className={`flex items-center justify-center min-w-[16px] h-4 px-1 rounded-full text-[9px] font-bold ${
                showLogs ? 'bg-white text-indigo-600' : 'bg-indigo-600 text-white'
              }`}>
                {logCount}
              </span>
            )}
          </button>
        )}
      </div>
      <div className="flex items-center gap-4">
        <span>Nodes: {nodeCount}</span>
        <span>Edges: {edgeCount}</span>
        <span>Zoom: {Math.round(zoom * 100)}%</span>
      </div>
    </footer>
  );
}
