import { Play, Square, Save, Undo2, Redo2, LayoutGrid, Settings, Download, Upload, PanelLeft, PanelRight, MessageCircle, Puzzle, RotateCcw, FolderOpen, ArrowLeftRight, ClipboardList, FolderTree, Globe, Keyboard } from 'lucide-react';
import { useExecutionStore } from '@stores/useExecutionStore';
import { useUndoRedoStore } from '@stores/useUndoRedoStore';
interface AppHeaderProps {
  isRunning: boolean;
  onRun: () => void;
  onSave: () => void;
  onSaveProject?: () => void;
  onLoadProject?: () => void;
  onTogglePalette: () => void;
  onToggleProperties: () => void;
  onToggleAiAssistant: () => void;
  onToggleAgentPanel: () => void;
  onToggleDataExchange?: () => void;
  onToggleWorkspace?: () => void;
  onToggleDynamicApi?: () => void;
  onToggleFormBuilder?: () => void;
  onToggleAiConfig: () => void;
  onToggleHelp?: () => void;
  onAutoLayout?: () => void;
  onResetFlow?: () => void;
  onImport?: () => void;
  onExport?: () => void;
  onLoad?: () => void;
  flowName?: string;
  onFlowNameChange?: (name: string) => void;
}

export function AppHeader({ isRunning, onRun, onSave, onSaveProject, onLoadProject, onTogglePalette, onToggleProperties, onToggleAiAssistant, onToggleAgentPanel, onToggleDataExchange, onToggleWorkspace, onToggleDynamicApi, onToggleFormBuilder, onToggleAiConfig, onToggleHelp, onAutoLayout, onResetFlow, onImport, onExport, onLoad, flowName, onFlowNameChange }: AppHeaderProps) {
  const canUndo = useUndoRedoStore((s) => s.canUndo);
  const canRedo = useUndoRedoStore((s) => s.canRedo);

  return (
    <header className="app-header">
      {/* Left: Logo + Flow Name */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2">
          <div className="w-7 h-7 rounded-lg bg-indigo-600 flex items-center justify-center">
            <LayoutGrid className="w-4 h-4 text-white" />
          </div>
          <span className="font-semibold text-sm text-gray-100">StepFlow Builder</span>
        </div>
        <div className="h-5 w-px bg-gray-700" />
        <input
          type="text"
          value={flowName}
          onChange={(e) => onFlowNameChange?.(e.target.value)}
          className="bg-transparent text-sm text-gray-200 placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 rounded px-1.5 py-0.5 w-32"
          placeholder="Flow name..."
        />
      </div>

      {/* Center: Execution Controls */}
      <div className="flex items-center gap-3">
        <button
          className={`btn gap-1.5 text-xs ${isRunning ? 'btn-danger' : 'btn-success'}`}
          title={isRunning ? 'Stop Execution' : 'Run Flow (Ctrl+Enter)'}
          onClick={onRun}
        >
          {isRunning ? (
            <Square className="w-3.5 h-3.5" />
          ) : (
            <Play className="w-3.5 h-3.5" />
          )}
          <span>{isRunning ? 'Stop' : 'Run'}</span>
        </button>

        {/* Execution Mode Toggle */}
        <div className="flex items-center gap-1 bg-gray-800/80 rounded-lg p-0.5 border border-gray-700/50">
          <button
            onClick={() => useExecutionStore.getState().setExecutionMode('simulated')}
            title="Runs in your browser. Nodes with a saved configuration (HTTP, SQL, AI) still call their real endpoints; everything else is simulated."
            className={`px-2 py-1 rounded-md text-[10px] font-semibold transition-all ${
              useExecutionStore((s) => s.executionMode) === 'simulated'
                ? 'bg-gray-700 text-white shadow-sm'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Local
          </button>
          <button
            onClick={() => useExecutionStore.getState().setExecutionMode('backend')}
            className={`px-2 py-1 rounded-md text-[10px] font-semibold transition-all ${
              useExecutionStore((s) => s.executionMode) === 'backend'
                ? 'bg-indigo-600 text-white shadow-sm'
                : 'text-gray-400 hover:text-gray-200'
            }`}
            title="Execute live using real .NET 10 background engine"
          >
            Live Backend
          </button>
        </div>
      </div>
      {/* Right: Actions */}
      <div className="flex items-center gap-1">
        <button
          className="btn btn-primary gap-1.5 text-xs"
          title="Save (Ctrl+S) — to the selected Workspace project, else this browser"
          onClick={onSave}
        >
          <Save className="w-3.5 h-3.5" />
          <span>Save</span>
        </button>
        {onSaveProject && (
          <button
            className="btn btn-primary gap-1.5 text-xs bg-indigo-600 hover:bg-indigo-500 border-indigo-500/20"
            title="Save as Multi-File Project Folder"
            onClick={onSaveProject}
          >
            <FolderOpen className="w-3.5 h-3.5" />
            <span>Save Project</span>
          </button>
        )}
        {onLoadProject && (
          <button
            className="btn btn-primary gap-1.5 text-xs bg-indigo-600 hover:bg-indigo-500 border-indigo-500/20"
            title="Load Multi-File Project Folder"
            onClick={onLoadProject}
          >
            <FolderOpen className="w-3.5 h-3.5" />
            <span>Load Project</span>
          </button>
        )}
        <button
          className={`btn-icon ${canUndo ? '' : 'opacity-40 cursor-not-allowed'}`}
          title="Undo (Ctrl+Z)"
          onClick={() => useUndoRedoStore.getState().undo()}
          disabled={!canUndo}
        >
          <Undo2 className="w-4 h-4" />
        </button>
        <button
          className={`btn-icon ${canRedo ? '' : 'opacity-40 cursor-not-allowed'}`}
          title="Redo (Ctrl+Y)"
          onClick={() => useUndoRedoStore.getState().redo()}
          disabled={!canRedo}
        >
          <Redo2 className="w-4 h-4" />
        </button>
        <button
          className="btn-icon"
          title="Auto-Layout (Ctrl+Shift+F)"
          onClick={onAutoLayout}
        >
          <LayoutGrid className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Reset Flow" onClick={onResetFlow}>
          <RotateCcw className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Toggle AI Assistant" onClick={onToggleAiAssistant}>
          <MessageCircle className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Toggle Agent Panel" onClick={onToggleAgentPanel}>
          <Puzzle className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Toggle Data Exchange Panel" onClick={onToggleDataExchange}>
          <ArrowLeftRight className="w-4 h-4" />
        </button>
        {onToggleWorkspace && (
          <button className="btn-icon" title="Toggle Workspace Panel" onClick={onToggleWorkspace}>
            <FolderTree className="w-4 h-4" />
          </button>
        )}
        {onToggleDynamicApi && (
          <button className="btn-icon" title="Toggle Dynamic API Panel" onClick={onToggleDynamicApi}>
            <Globe className="w-4 h-4" />
          </button>
        )}
        <button className="btn-icon" title="Open Form Builder" onClick={onToggleFormBuilder}>
          <ClipboardList className="w-4 h-4" />
        </button>
        <div className="h-5 w-px bg-gray-700" />
        <button className="btn-icon" title="Toggle Palette" onClick={onTogglePalette}>
          <PanelLeft className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Toggle Properties" onClick={onToggleProperties}>
          <PanelRight className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Import Flow" onClick={onImport}>
          <Upload className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Export Flow" onClick={onExport}>
          <Download className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Load Flow" onClick={onLoad}>
          <FolderOpen className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="AI Model Configuration" onClick={onToggleAiConfig}>
          <Settings className="w-4 h-4" />
        </button>
        {onToggleHelp && (
          <button className="btn-icon" title="Help & Keyboard Shortcuts" onClick={onToggleHelp}>
            <Keyboard className="w-4 h-4" />
          </button>
        )}
      </div>
    </header>
  );
}
