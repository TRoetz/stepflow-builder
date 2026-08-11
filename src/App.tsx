import { useState, useCallback } from 'react';
import { FlowCanvas } from '@components/Canvas/FlowCanvas';
import { NodePalette } from '@components/Palette/NodePalette';
import { PropertyPanel } from '@components/Properties/PropertyPanel';
import { AppHeader } from '@components/Header/AppHeader';
import { StatusBar } from '@components/Header/StatusBar';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useNodeStore } from '@stores/useNodeStore';
import { useExecutionStore } from '@stores/useExecutionStore';
import { schemaById } from '@schemas/index';
import { ExecutionService } from '@services/executionService';
import { FlowService } from '@services/flowService';
import { useAutoLayout } from '@hooks/useAutoLayout';
import { CanvasAssistant } from '@components/Canvas/CanvasAssistant';
import { AgentPanel } from '@components/Agents/AgentPanel';
import { useAiAssistantStore } from '@stores/useAiAssistantStore';
import { summarizeConfigFields } from '@stores/aiAssistantPrompts';
import { useAiModelConfigStore } from '@stores/useAiModelConfigStore';
import { AiModelConfigModal } from '@components/AiModelConfigModal';
import { ErrorBoundary } from '@components/ErrorBoundary';
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
  const aiAssistantOpen = useAiAssistantStore((s) => s.isOpen);
  const toggleAiAssistant = useAiAssistantStore((s) => s.toggleOpen);
  const toggleAiConfig = useAiModelConfigStore((s) => s.toggleConfigModal);

  const { autoLayout } = useAutoLayout();
  const executionStatus = useExecutionStore((s) => s.status);

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
    await FlowService.saveFlow(flowName);
    console.log(`Flow "${flowName}" saved!`);
  }, [flowName]);

  // ── Handle Auto-Layout ──
  const handleAutoLayout = useCallback(async () => {
    await autoLayout();
  }, [autoLayout]);

  // ── Handle Reset Flow ──
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
          console.log('Flow imported successfully');
        } catch (err) {
          console.error('Failed to import flow:', err);
          alert('Failed to import flow. Please check the file format.');
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
      setShowLoadDialog(false);
      console.log('Flow loaded successfully');
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
        <div className="app-canvas">
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
                    <button
                      key={flow.id}
                      className="w-full text-left px-3 py-2.5 rounded-lg bg-gray-800/50 hover:bg-gray-800 border border-gray-700/50 hover:border-gray-600 transition-all group"
                      onClick={() => loadSelectedFlow(flow.id)}
                    >
                      <div className="text-sm font-medium text-gray-200 group-hover:text-white">{flow.name}</div>
                      {flow.description && (
                        <div className="text-xs text-gray-500 mt-0.5">{flow.description}</div>
                      )}
                      <div className="text-[10px] text-gray-600 mt-1">
                        {new Date(flow.createdAt).toLocaleString()}
                      </div>
                    </button>
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
    </div>
  );
}
