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
          <FlowCanvas
            onNodeSelect={handleNodeSelect}
            onNodeCountChange={setNodeCount}
            onEdgeCountChange={setEdgeCount}
            onZoomChange={setZoom}
          />
          {aiAssistantOpen && <CanvasAssistant />}
        </div>

        {/* Right: Property Panel */}
        {!isCollapsedProperties && (
          <div className="app-properties">
            <PropertyPanel selectedNode={selectedNode} />
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
    </div>
  );
}
