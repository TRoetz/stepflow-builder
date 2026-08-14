import { useState, useCallback, useEffect, useMemo } from 'react';
import { FlowCanvas } from '@components/Canvas/FlowCanvas';
import { NodePalette } from '@components/Palette/NodePalette';
import { PropertyPanel } from '@components/Properties/PropertyPanel';
import { AppHeader } from '@components/Header/AppHeader';
import { StatusBar } from '@components/Header/StatusBar';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useExecutionStore, type StepExecutionLog } from '@stores/useExecutionStore';
import { schemaById } from '@schemas/index';
import { ExecutionService } from '@services/executionService';
import { FlowService } from '@services/flowService';
import { useAutoLayout } from '@hooks/useAutoLayout';
import { useKeyboardShortcuts } from '@hooks/useKeyboardShortcuts';
import { CanvasAssistant } from '@components/Canvas/CanvasAssistant';
import { AgentPanel } from '@components/Agents/AgentPanel';
import { useAiAssistantStore } from '@stores/useAiAssistantStore';
import { summarizeConfigFields } from '@stores/aiAssistantPrompts';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import { AiModelConfigModal } from '@components/AiModelConfigModal';
import { ErrorBoundary } from '@components/ErrorBoundary';
import { CheckCircle2, AlertCircle, Trash2, Terminal, Code, FolderOpen } from 'lucide-react';
import './styles/globals.css';
export default function App() {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [nodeCount, setNodeCount] = useState(0);
  const [edgeCount, setEdgeCount] = useState(0);
  const [zoom, setZoom] = useState(1);
  const [isCollapsedPalette, setIsCollapsedPalette] = useState(false);
  const [isCollapsedProperties, setIsCollapsedProperties] = useState(false);
  const [flowName, setFlowName] = useState('New Flow');
  const [showAgentPanel, setShowAgentPanel] = useState(false);
  const [toast, setToast] = useState<{ type: 'success' | 'error'; message: string } | null>(null);
  const [showLogPanel, setShowLogPanel] = useState(false);
  const [expandedLogNodeId, setExpandedLogNodeId] = useState<string | null>(null);
  const [showSaveProjectDialog, setShowSaveProjectDialog] = useState(false);
  const [showLoadProjectDialog, setShowLoadProjectDialog] = useState(false);
  const [projectDirectoryPath, setProjectDirectoryPath] = useState('C:\\temp\\StepFlowProject');
  const executionLogs = useExecutionStore((s) => s.logs);
  const logsList = useMemo(() => {
    return Object.values(executionLogs).sort((a, b) => a.startTime - b.startTime);
  }, [executionLogs]);
  const aiAssistantOpen = useAiAssistantStore((s) => s.isOpen);
  const toggleAiAssistant = useAiAssistantStore((s) => s.toggleOpen);
  const toggleAiConfig = useAiModelConfigStore((s) => s.toggleConfigModal);

  const { autoLayout } = useAutoLayout();
  const executionStatus = useExecutionStore((s) => s.status);

  // Auto-hide toast after 3 seconds
  useEffect(() => {
    if (toast) {
      const timer = setTimeout(() => setToast(null), 3000);
      return () => clearTimeout(timer);
    }
  }, [toast]);

  // Register setToast on window for global access (e.g., execution service)
  useEffect(() => {
    (window as any).__setToast = setToast;
    return () => {
      delete (window as any).__setToast;
    };
  }, []);

  // Register flowName on window so the execution service can read the current flow's name
  useEffect(() => {
    (window as any).__flowName = flowName;
    return () => {
      delete (window as any).__flowName;
    };
  }, [flowName]);
  // ── Handle Node Add (from palette) ──
  const handleNodeAdd = useCallback(
    (schemaId: string) => {
      const schema = schemaById.get(schemaId);
      if (!schema) {
        console.warn(`Schema not found: ${schemaId}`);
        return;
      }
      useNodeStore.getState().addNode(schemaId);
      const notifyNodeAdded = useAiAssistantStore.getState().notifyNodeAdded;
      if (schema) {
        notifyNodeAdded(schema.name, schema.category);
      }
    },
    []
  );

  // ── Handle Node Select ──
  const handleNodeSelect = useCallback((nodeId: string | null) => {
    setSelectedNodeId(nodeId);
    if (nodeId) {
      const node = useNodeStore.getState().nodes.find((n) => n.id === nodeId);
      if (node) {
        const schema = schemaById.get(node.data?.schemaId);
        const notify = useAiAssistantStore.getState().notifyNodeSelected;
        if (schema) {
          notify(
            schema.name,
            schema.schemaId,
            schema.category,
            summarizeConfigFields(schema.configFields)
          );
        } else {
          notify(node.data?.label || null);
        }
      }
    } else {
      useAiAssistantStore.getState().notifyNodeSelected(null);
    }
  }, []);

  // ── Handle Run/Stop ──
  const handleRun = useCallback(async () => {
    if (executionStatus === 'running' || executionStatus === 'paused') {
      await ExecutionService.stopExecution();
    } else {
      await ExecutionService.startExecution();
    }
  }, [executionStatus]);

  // ── Handle Save ──
  const handleSave = useCallback(async () => {
    const res = await FlowService.saveFlow(flowName);
    setToast({ type: res.success ? 'success' : 'error', message: res.message });
  }, [flowName]);

  // Global Keyboard Shortcuts
  useKeyboardShortcuts({
    onAutoLayout: autoLayout,
    onSave: handleSave,
    onRun: handleRun,
  });
  // ── Handle Auto-Layout ──
  const handleAutoLayout = useCallback(async () => {
    await autoLayout();
  }, [autoLayout]);

  // ── Handle Save Project ──
  const handleSaveProject = useCallback(async () => {
    try {
      const layout = {
        nodes: useNodeStore.getState().nodes,
        edges: useEdgeStore.getState().edges
      };
      const stepflow = FlowService.exportFlow();

      const response = await fetch('/api/state-machines/save-project', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: flowName,
          directoryPath: projectDirectoryPath,
          layout,
          stepflow
        })
      });

      if (!response.ok) {
        const errData = await response.json();
        throw new Error(errData.error || 'Failed to save project');
      }

      const result = await response.json();
      setToast({ type: 'success', message: result.message || 'Project saved successfully!' });
      setShowSaveProjectDialog(false);
    } catch (err: any) {
      console.error('Failed to save project:', err);
      setToast({ type: 'error', message: `Save failed: ${err.message}` });
    }
  }, [flowName, projectDirectoryPath]);

  // ── Handle Load Project ──
  const handleLoadProject = useCallback(async () => {
    try {
      const response = await fetch('/api/state-machines/load-project', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          directoryPath: projectDirectoryPath
        })
      });

      if (!response.ok) {
        const errData = await response.json();
        throw new Error(errData.error || 'Failed to load project');
      }

      const result = await response.json();
      const layout = result.layout;
      
      if (layout && layout.nodes && layout.edges) {
        // Clear canvas
        useNodeStore.getState().clearNodes();
        useEdgeStore.getState().clearEdges();
        
        // Load layout nodes and edges
        useNodeStore.setState({ nodes: layout.nodes });
        useEdgeStore.setState({ edges: layout.edges });
        
        if (result.name) {
          setFlowName(result.name);
        }
        
        // Auto-layout or run layout
        setTimeout(() => {
          autoLayout();
        }, 50);

        setToast({ type: 'success', message: result.message || 'Project loaded successfully!' });
        setShowLoadProjectDialog(false);
      } else {
        throw new Error('Invalid project layout format');
      }
    } catch (err: any) {
      console.error('Failed to load project:', err);
      setToast({ type: 'error', message: `Load failed: ${err.message}` });
    }
  }, [projectDirectoryPath, autoLayout]);

  // ── Handle Client-Side Folder Selection ──
  const handleFolderSelect = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;

    let projectFile: File | null = null;
    let layoutFile: File | null = null;

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const name = file.name;
      if (name === 'project.json') projectFile = file;
      if (name === 'layout.json') layoutFile = file;
    }

    if (!projectFile || !layoutFile) {
      setToast({ type: 'error', message: 'Selected folder must contain project.json and layout.json' });
      return;
    }

    const projectReader = new FileReader();
    projectReader.onerror = () => {
      setToast({ type: 'error', message: 'Failed to read project.json' });
    };
    projectReader.onload = (evProj) => {
      try {
        const projectData = JSON.parse(evProj.target?.result as string);
        const layoutReader = new FileReader();
        layoutReader.onerror = () => {
          setToast({ type: 'error', message: 'Failed to read layout.json' });
        };
        layoutReader.onload = (evLayout) => {
          try {
            const layoutData = JSON.parse(evLayout.target?.result as string);
            
            if (layoutData && layoutData.nodes && layoutData.edges) {
              // Clear canvas
              useNodeStore.getState().clearNodes();
              useEdgeStore.getState().clearEdges();
              
              // Load nodes and edges
              useNodeStore.setState({ nodes: layoutData.nodes });
              useEdgeStore.setState({ edges: layoutData.edges });
              
              if (projectData.name) {
                setFlowName(projectData.name);
              }
              
              setTimeout(() => {
                autoLayout();
              }, 50);

              setToast({ type: 'success', message: 'Project folder loaded successfully!' });
              setShowLoadProjectDialog(false);
            } else {
              setToast({ type: 'error', message: 'Invalid layout.json format inside selected folder.' });
            }
          } catch {
            setToast({ type: 'error', message: 'Failed to parse layout.json' });
          }
        };
        layoutReader.readAsText(layoutFile!);
      } catch {
        setToast({ type: 'error', message: 'Failed to parse project.json' });
      }
    };
    projectReader.readAsText(projectFile);
  }, [autoLayout]);
  const handleResetFlow = useCallback(() => {
    useNodeStore.getState().clearNodes();
    useEdgeStore.getState().clearEdges();
  }, []);

  // ── Handle Import Flow ──
  const handleImport = useCallback(() => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json';
    input.onchange = (e: Event) => {
      const target = e.target as HTMLInputElement;
      const file = target.files?.[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = (ev) => {
        try {
          const definition = JSON.parse(ev.target?.result as string);
          FlowService.importFlow(definition);
          setTimeout(() => {
            autoLayout();
          }, 50);
          // Set flow name from imported file if available
          if (definition && typeof definition === 'object' && 'name' in definition) {
            setFlowName((definition as Record<string, unknown>).name as string);
          }
          setToast({ type: 'success', message: 'Flow imported successfully!' });
        } catch (err) {
          console.error('Failed to import flow:', err);
          setToast({ type: 'error', message: 'Failed to import flow. Please check the file format.' });
        }
      };
      reader.readAsText(file);
    };
    input.click();
  }, []);

  // ── Handle Export Flow ──
  const handleExport = useCallback(() => {
    const definition = FlowService.exportFlow();
    const json = JSON.stringify(definition, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${flowName.replace(/\s+/g, '_')}.json`;
    a.click();
    URL.revokeObjectURL(url);
    console.log('Flow exported successfully');
  }, [flowName]);

  // ── Handle Load Flow ──
  const [savedFlows, setSavedFlows] = useState<Array<{ id: string; name: string; description?: string; createdAt: string }>>([]);
  const [showLoadDialog, setShowLoadDialog] = useState(false);

  const handleLoad = useCallback(async () => {
    const flows = await FlowService.listFlows();
    setSavedFlows(flows);
    setShowLoadDialog(true);
  }, []);

  const loadSelectedFlow = useCallback(async (flowId: string) => {
    const definition = await FlowService.loadFlow(flowId);
    if (definition) {
      FlowService.importFlow(definition);
      setTimeout(() => {
        autoLayout();
      }, 50);
      const selected = savedFlows.find((f) => f.id === flowId);
      if (selected) {
        setFlowName(selected.name);
      }
      setShowLoadDialog(false);
      setToast({ type: 'success', message: 'Flow loaded successfully!' });
    }
  }, [savedFlows]);

  const deleteSelectedFlow = useCallback(async (e: React.MouseEvent, flowId: string) => {
    e.stopPropagation();
    try {
      const saved = localStorage.getItem('stepflow-flows');
      const flows = saved ? JSON.parse(saved) : [];
      const updated = flows.filter((f: { id: string }) => f.id !== flowId);
      localStorage.setItem('stepflow-flows', JSON.stringify(updated));
      setSavedFlows(updated.map((f: { id: string; name: string; description?: string; createdAt: string }) => ({
        id: f.id,
        name: f.name,
        description: f.description,
        createdAt: f.createdAt,
      })));
      setToast({ type: 'success', message: 'Flow deleted' });
    } catch {
      setToast({ type: 'error', message: 'Failed to delete flow' });
    }
  }, []);
  // ── Selected Node Data ──
  const selectedNode = useNodeStore((state) =>
    selectedNodeId ? state.nodes.find((n) => n.id === selectedNodeId) : undefined
  );

  return (
    <div className="flex flex-col h-screen bg-gray-950 text-gray-100 overflow-hidden">
      <AppHeader
        isRunning={executionStatus === 'running' || executionStatus === 'paused'}
        onRun={handleRun}
        onSave={handleSave}
        onSaveProject={() => setShowSaveProjectDialog(true)}
        onLoadProject={() => setShowLoadProjectDialog(true)}
        onTogglePalette={() => setIsCollapsedPalette((p) => !p)}
        onToggleProperties={() => setIsCollapsedProperties((p) => !p)}
        onToggleAiAssistant={toggleAiAssistant}
        onToggleAgentPanel={() => setShowAgentPanel((p) => !p)}
        onToggleAiConfig={toggleAiConfig}
        onAutoLayout={handleAutoLayout}
        onResetFlow={handleResetFlow}
        onImport={handleImport}
        onExport={handleExport}
        onLoad={handleLoad}
        flowName={flowName}
        onFlowNameChange={setFlowName}
      />

      <div className="flex-1 flex overflow-hidden">
        {/* Left: Node Palette */}
        {!isCollapsedPalette && (
          <div className="app-palette">
            <NodePalette onNodeAdd={handleNodeAdd} />
          </div>
        )}

        {/* Center: Canvas */}
        <div className="app-canvas flex flex-col h-full relative">
          <div className="flex-1 relative overflow-hidden">
            <ErrorBoundary>
              <FlowCanvas
                onNodeSelect={handleNodeSelect}
                onNodeCountChange={setNodeCount}
                onEdgeCountChange={setEdgeCount}
                onZoomChange={setZoom}
              />
            </ErrorBoundary>
            {aiAssistantOpen && <CanvasAssistant />}
          </div>

          {/* Bottom Execution Log Drawer */}
          {showLogPanel && (
            <div className="h-72 border-t border-gray-800 bg-gray-900/90 flex flex-col overflow-hidden backdrop-blur-md">
              {/* Header */}
              <div className="flex items-center justify-between px-4 py-2 border-b border-gray-800 bg-gray-950/60">
                <div className="flex items-center gap-2 text-xs font-semibold text-gray-300">
                  <Terminal className="w-4 h-4 text-indigo-400" />
                  <span>Flow Execution Log History</span>
                </div>
                <button
                  onClick={() => setShowLogPanel(false)}
                  className="text-gray-400 hover:text-gray-200 text-xs transition-colors"
                  title="Close logs"
                >
                  ✕
                </button>
              </div>

              {/* Logs Content List */}
              <div className="flex-1 flex overflow-hidden">
                {/* Left: Step execution list */}
                <div className="w-1/3 border-r border-gray-800 overflow-y-auto divide-y divide-gray-800/50">
                  {logsList.length === 0 ? (
                    <div className="text-xs text-gray-500 text-center py-12">
                      No steps executed yet. Run the flow to populate logs.
                    </div>
                  ) : (
                    logsList.map((log: StepExecutionLog) => (
                      <button
                        key={log.nodeId}
                        onClick={() => setExpandedLogNodeId(log.nodeId === expandedLogNodeId ? null : log.nodeId)}
                        className={`w-full text-left px-4 py-2.5 flex items-center justify-between transition-colors ${
                          expandedLogNodeId === log.nodeId
                            ? 'bg-indigo-600/10 hover:bg-indigo-600/15'
                            : 'hover:bg-gray-800/40'
                        }`}
                      >
                        <div className="min-w-0 flex-1 pr-2">
                          <div className="text-xs font-semibold text-gray-200 truncate">{log.nodeName}</div>
                          <div className="text-[10px] text-gray-500 font-mono mt-0.5">
                            {new Date(log.startTime).toLocaleTimeString()}
                          </div>
                        </div>
                        <div className="flex items-center gap-2 shrink-0">
                          <span className={`px-1.5 py-0.5 rounded text-[8px] font-bold ${
                            log.status === 'completed'
                              ? 'bg-green-500/10 text-green-400'
                              : log.status === 'failed'
                                ? 'bg-red-500/10 text-red-400'
                                : 'bg-amber-500/10 text-amber-400'
                          }`}>
                            {log.status.toUpperCase()}
                          </span>
                        </div>
                      </button>
                    ))
                  )}
                </div>

                {/* Right: Selected step detailed payload comparison */}
                <div className="flex-1 overflow-y-auto p-4 bg-gray-950/20 font-mono text-[11px] leading-relaxed">
                  {expandedLogNodeId && executionLogs[expandedLogNodeId] ? (
                    (() => {
                      const log = executionLogs[expandedLogNodeId];
                      return (
                        <div className="space-y-4">
                          <div className="flex items-center justify-between">
                            <h4 className="text-xs font-bold text-gray-300">Detailed Node Payloads — {log.nodeName}</h4>
                            <span className="text-[10px] text-gray-500">Duration: {log.endTime ? `${log.endTime - log.startTime}ms` : 'Running...'}</span>
                          </div>

                          {log.error && (
                            <div className="border border-red-500/20 bg-red-500/5 rounded-lg p-3 text-red-400 text-xs font-sans whitespace-pre-wrap leading-normal">
                              <strong>Execution Error: </strong> {log.error}
                            </div>
                          )}

                          <div className="grid grid-cols-2 gap-4">
                            {/* Input Data Payload */}
                            <div className="space-y-1.5">
                              <div className="text-[10px] text-gray-500 uppercase tracking-wider font-semibold flex items-center gap-1.5">
                                <Code className="w-3.5 h-3.5" />
                                <span>Input Payload</span>
                              </div>
                              <pre className="p-3 rounded-xl bg-black/40 border border-gray-800/80 overflow-x-auto max-h-48 text-[10px] leading-normal text-gray-300">
                                {JSON.stringify(log.input, null, 2)}
                              </pre>
                            </div>

                            {/* Output Data Payload */}
                            <div className="space-y-1.5">
                              <div className="text-[10px] text-gray-500 uppercase tracking-wider font-semibold flex items-center gap-1.5">
                                <Terminal className="w-3.5 h-3.5" />
                                <span>Output Payload</span>
                              </div>
                              <pre className="p-3 rounded-xl bg-black/40 border border-gray-800/80 overflow-x-auto max-h-48 text-[10px] leading-normal text-gray-300">
                                {JSON.stringify(log.output, null, 2)}
                              </pre>
                            </div>
                          </div>
                        </div>
                      );
                    })()
                  ) : (
                    <div className="h-full flex items-center justify-center text-xs text-gray-500">
                      Select a step on the left to inspect its detailed input/output JSON payloads.
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Right: Property Panel */}
        {!isCollapsedProperties && (
          <div className="app-properties">
            <ErrorBoundary>
              <PropertyPanel selectedNode={selectedNode} />
            </ErrorBoundary>
          </div>
        )}
        {showAgentPanel && (
          <div className="app-properties">
            <AgentPanel />
          </div>
        )}
      </div>

      <StatusBar
        isRunning={executionStatus === 'running' || executionStatus === 'paused'}
        nodeCount={nodeCount}
        edgeCount={edgeCount}
        zoom={zoom}
        executionStatus={executionStatus}
        showLogs={showLogPanel}
        onToggleLogs={() => setShowLogPanel((p) => !p)}
        logCount={logsList.length}
      />
      <AiModelConfigModal />

      {/* Load Flow Dialog */}
      {showLoadDialog && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-md mx-4 shadow-2xl">
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700">
              <h3 className="text-sm font-semibold text-gray-100">Load Saved Flow</h3>
              <button
                className="text-gray-400 hover:text-gray-200 transition-colors"
                onClick={() => setShowLoadDialog(false)}
              >
                ✕
              </button>
            </div>

            <div className="px-4 py-3 max-h-80 overflow-y-auto">
              {savedFlows.length === 0 ? (
                <div className="text-sm text-gray-500 text-center py-6">
                  No saved flows found. Save a flow first.
                </div>
              ) : (
                <div className="space-y-2">
                  {savedFlows.map((flow) => (
                    <div
                      key={flow.id}
                      className="flex items-center justify-between px-3 py-2.5 rounded-lg bg-gray-800/50 hover:bg-gray-800 border border-gray-700/50 hover:border-gray-600 transition-all group cursor-pointer"
                      onClick={() => loadSelectedFlow(flow.id)}
                    >
                      <div className="flex-1 min-w-0 pr-2">
                        <div className="text-sm font-medium text-gray-200 group-hover:text-white truncate">{flow.name}</div>
                        {flow.description && (
                          <div className="text-xs text-gray-500 mt-0.5 truncate">{flow.description}</div>
                        )}
                        <div className="text-[10px] text-gray-600 mt-1">
                          {new Date(flow.createdAt).toLocaleString()}
                        </div>
                      </div>
                      <button
                        className="p-1.5 rounded-md text-gray-500 hover:text-red-400 hover:bg-red-500/10 transition-colors"
                        title="Delete saved flow"
                        onClick={(e) => deleteSelectedFlow(e, flow.id)}
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="flex justify-end px-4 py-3 border-t border-gray-700">
              <button
                className="px-3 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 transition-colors"
                onClick={() => setShowLoadDialog(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Save Project Structure Dialog */}
      {showSaveProjectDialog && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-md mx-4 shadow-2xl overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700 bg-gray-950/40">
              <h3 className="text-sm font-semibold text-gray-100 flex items-center gap-1.5">
                <Terminal className="w-4 h-4 text-indigo-400" />
                <span>Save Multi-File Project Folder</span>
              </h3>
              <button
                className="text-gray-400 hover:text-gray-200 transition-colors text-xs"
                onClick={() => setShowSaveProjectDialog(false)}
              >
                ✕
              </button>
            </div>

            <div className="p-4 space-y-4">
              <div className="text-xs text-gray-400 leading-normal">
                This compiles and saves your active flow design as a fully-operational, multi-file local Project Folder containing:
                <ul className="list-disc pl-5 mt-2 space-y-1 font-semibold text-[10px] text-gray-500 font-mono">
                  <li>project.json (Metadata & links)</li>
                  <li>layout.json (UI coordinates)</li>
                  <li>stepflow.json (Standard ASL steps)</li>
                  <li>environments.json (DEV/TEST/PROD)</li>
                  <li>parameters.json (Input variables)</li>
                  <li>test_suite.json (Step verifications)</li>
                </ul>
              </div>

              <div className="space-y-1.5">
                <label className="text-xs text-gray-500 font-semibold">Local Directory Path</label>
                <input
                  type="text"
                  value={projectDirectoryPath}
                  onChange={(e) => setProjectDirectoryPath(e.target.value)}
                  placeholder="e.g., C:\temp\StepFlowProject"
                  className="w-full bg-gray-950 text-xs px-3 py-2 rounded-lg border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500 font-mono"
                />
              </div>
            </div>

            <div className="flex justify-end gap-2 px-4 py-3 bg-gray-950/20 border-t border-gray-800">
              <button
                className="px-3 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 transition-colors"
                onClick={() => setShowSaveProjectDialog(false)}
              >
                Cancel
              </button>
              <button
                className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white transition-colors"
                onClick={handleSaveProject}
              >
                Save Project Folder
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Load Project Structure Dialog */}
      {showLoadProjectDialog && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60">
          <div className="bg-gray-900 border border-gray-700 rounded-xl w-full max-w-md mx-4 shadow-2xl overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700 bg-gray-950/40">
              <h3 className="text-sm font-semibold text-gray-100 flex items-center gap-1.5">
                <Terminal className="w-4 h-4 text-indigo-400" />
                <span>Load Multi-File Project Folder</span>
              </h3>
              <button
                className="text-gray-400 hover:text-gray-200 transition-colors text-xs"
                onClick={() => setShowLoadProjectDialog(false)}
              >
                ✕
              </button>
            </div>

            <div className="p-4 space-y-4">
              <div className="text-xs text-gray-400 leading-normal">
                This loads a compiled multi-file local Project Folder by reading:
                <ul className="list-disc pl-5 mt-2 space-y-1 font-semibold text-[10px] text-gray-500 font-mono">
                  <li>project.json (Loads metadata & project name)</li>
                  <li>layout.json (Reconstructs visual canvas coordinates, nodes, and edges)</li>
                </ul>
              </div>

              <div className="space-y-1.5">
                <label className="text-xs text-gray-500 font-semibold block">Select Local Folder</label>
                <button
                  type="button"
                  className="w-full flex items-center justify-center gap-1.5 px-3 py-2 text-xs rounded-lg bg-indigo-600/10 hover:bg-indigo-600/20 text-indigo-400 border border-indigo-500/20 transition-colors font-medium"
                  onClick={() => document.getElementById('project-folder-picker')?.click()}
                >
                  <FolderOpen className="w-3.5 h-3.5" />
                  <span>Choose Local Project Folder...</span>
                </button>
                <input
                  type="file"
                  id="project-folder-picker"
                  className="hidden"
                  {...{ webkitdirectory: "", directory: "" }}
                  multiple
                  onChange={handleFolderSelect}
                />
              </div>

              <div className="flex items-center gap-2 py-1">
                <div className="flex-1 h-px bg-gray-800" />
                <span className="text-[10px] text-gray-600 font-semibold uppercase tracking-wider">or load by path</span>
                <div className="flex-1 h-px bg-gray-800" />
              </div>

              <div className="space-y-1.5">
                <label className="text-xs text-gray-500 font-semibold">Local Directory Path</label>
                <input
                  type="text"
                  value={projectDirectoryPath}
                  onChange={(e) => setProjectDirectoryPath(e.target.value)}
                  placeholder="e.g., C:\temp\StepFlowProject"
                  className="w-full bg-gray-950 text-xs px-3 py-2 rounded-lg border border-gray-800 text-gray-200 focus:outline-none focus:border-indigo-500 font-mono"
                />
              </div>
            </div>

            <div className="flex justify-end gap-2 px-4 py-3 bg-gray-950/20 border-t border-gray-800">
              <button
                className="px-3 py-1.5 text-xs rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 transition-colors"
                onClick={() => setShowLoadProjectDialog(false)}
              >
                Cancel
              </button>
              <button
                className="px-3 py-1.5 text-xs font-semibold rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white transition-colors"
                onClick={handleLoadProject}
              >
                Load Project Folder
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Toast Notification Banner */}
      {toast && (
        <div className={`fixed bottom-10 right-6 z-[100] flex items-center gap-2 px-4 py-2.5 rounded-xl shadow-xl border text-xs font-medium backdrop-blur-md transition-all duration-300 ${
          toast.type === 'success'
            ? 'bg-emerald-950/90 border-emerald-500/40 text-emerald-200'
            : 'bg-red-950/90 border-red-500/40 text-red-200'
        }`}>
          {toast.type === 'success' ? (
            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
          ) : (
            <AlertCircle className="w-4 h-4 text-red-400" />
          )}
          <span>{toast.message}</span>
        </div>
      )}
    </div>
  );
}
