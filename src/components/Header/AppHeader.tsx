import { Play, Square, Save, Undo2, Redo2, LayoutGrid, Settings, Download, Upload, PanelLeft, PanelRight, MessageCircle, Puzzle, RotateCcw, FolderOpen } from 'lucide-react';
interface AppHeaderProps {
  isRunning: boolean;
  onRun: () => void;
  onSave: () => void;
  onTogglePalette: () => void;
  onToggleProperties: () => void;
  onToggleAiAssistant: () => void;
  onToggleAgentPanel: () => void;
  onToggleAiConfig: () => void;
  onAutoLayout?: () => void;
  onResetFlow?: () => void;
  onImport?: () => void;
  onExport?: () => void;
  onLoad?: () => void;
  flowName?: string;
  onFlowNameChange?: (name: string) => void;
}

export function AppHeader({ isRunning, onRun, onSave, onTogglePalette, onToggleProperties, onToggleAiAssistant, onToggleAgentPanel, onToggleAiConfig, onAutoLayout, onResetFlow, onImport, onExport, onLoad, flowName, onFlowNameChange }: AppHeaderProps) {
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
      <div className="flex items-center gap-1">
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
      </div>

      {/* Right: Actions */}
      <div className="flex items-center gap-1">
        <button
          className="btn btn-primary gap-1.5 text-xs"
          title="Save Flow (Ctrl+S)"
          onClick={onSave}
        >
          <Save className="w-3.5 h-3.5" />
          <span>Save</span>
        </button>
        <button className="btn-icon" title="Undo (Ctrl+Z)">
          <Undo2 className="w-4 h-4" />
        </button>
        <button className="btn-icon" title="Redo (Ctrl+Y)">
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
      </div>
    </header>
  );
}
