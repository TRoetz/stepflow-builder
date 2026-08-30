import { useState } from 'react';
import { CheckCircle, Activity, AlertCircle, Loader2, Terminal, X, Plus } from 'lucide-react';
import { useFlowValidation } from '@hooks/useFlowValidation';
import { useNodeStore } from '@stores/useNodeStore';
import { useCanvasUiStore } from '@stores/useCanvasUiStore';

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
  const issues = useFlowValidation();
  const selectedNodeId = useNodeStore((s) => s.selectedNodeId);
  const selectedLabel = useNodeStore(
    (s) => s.nodes.find((n) => n.id === s.selectedNodeId)?.data.label
  );
  const [showIssues, setShowIssues] = useState(false);

  return (
    <footer className="app-statusbar relative">
      <div className="flex items-center gap-4">
        <span className="flex items-center gap-1.5">
          <Activity className="w-3.5 h-3.5" />
          Execution: <span className={status.color}>{status.label}</span>
        </span>
        {issues.length === 0 ? (
          <span className="flex items-center gap-1.5">
            <CheckCircle className="w-3.5 h-3.5 text-green-500" />
            Valid
          </span>
        ) : (
          <button
            onClick={() => setShowIssues((v) => !v)}
            title={`${issues.length} node${issues.length > 1 ? 's' : ''} with validation issues — click to review`}
            className="flex items-center gap-1.5 text-red-400 hover:text-red-300 transition-colors"
          >
            <AlertCircle className="w-3.5 h-3.5" />
            {issues.length} issue{issues.length > 1 ? 's' : ''}
          </button>
        )}

        {selectedNodeId && (
          <button
            onClick={(e) => {
              // Anchor to the button's top-right corner; the popover auto-flips
              // upward because there is no room below the status bar.
              const r = e.currentTarget.getBoundingClientRect();
              useCanvasUiStore.getState().openAddNext(selectedNodeId, r.right, r.top - 4);
            }}
            title={`Insert a step connected after "${selectedLabel || 'the selected node'}"`}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[10px] font-semibold bg-indigo-600/80 hover:bg-indigo-500 border border-indigo-500/60 text-white transition-colors"
          >
            <Plus className="w-3 h-3" />
            <span>Add Next</span>
          </button>
        )}

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

      {showIssues && issues.length > 0 && (
        <div className="absolute bottom-full left-4 mb-1.5 w-[28rem] max-h-72 overflow-auto rounded-lg border border-gray-700 bg-gray-900 shadow-xl z-50">
          <div className="flex items-center justify-between px-3 py-2 border-b border-gray-800 sticky top-0 bg-gray-900">
            <span className="text-xs font-semibold text-red-400">
              {issues.length} validation issue{issues.length > 1 ? 's' : ''}
            </span>
            <button
              onClick={() => setShowIssues(false)}
              className="p-1 rounded hover:bg-gray-800 text-gray-500 hover:text-gray-300"
              title="Close"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
          <ul className="p-2 space-y-1">
            {issues.map((issue) => (
              <li key={issue.nodeId}>
                <button
                  onClick={() => useNodeStore.getState().setSelectedNode(issue.nodeId)}
                  title={`Select "${issue.label}"`}
                  className="w-full text-left px-2 py-1.5 rounded-md hover:bg-gray-800 transition-colors"
                >
                  <div className="text-xs font-medium text-gray-200">{issue.label}</div>
                  {issue.errors.map((error, i) => (
                    <div key={i} className="text-[11px] text-red-400/90 pl-3">
                      • {error}
                    </div>
                  ))}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </footer>
  );
}
