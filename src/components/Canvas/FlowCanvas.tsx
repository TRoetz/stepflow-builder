import { useCallback, useRef, useMemo, useState } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  MarkerType,
  Connection,
  Edge,
  Node,
  useReactFlow,
} from '@xyflow/react';
import { v4 as uuidv4 } from 'uuid';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import { useExecutionStore } from '@stores/useExecutionStore';
import { useSettingsStore } from '@stores/useSettingsStore';
import { getStepNodeTypes } from '@schemas/index';
import { AiNode } from '@components/Nodes/AiNode';
import { RuleNode } from '@components/Nodes/RuleNode';
import { DataNode } from '@components/Nodes/DataNode';
import { ApiNode } from '@components/Nodes/ApiNode';
import { TransformNode } from '@components/Nodes/TransformNode';
import { UtilityNode } from '@components/Nodes/UtilityNode';
import { SubFlowNode } from '@components/Nodes/SubFlowNode';
import { TerminalNode } from '@components/Nodes/TerminalNode';
import { FlowNode } from '@components/Nodes/FlowNode';
import { DefaultNode } from '@components/Nodes/DefaultNode';
import { StepEdge } from './StepEdge';
import { useAutoLayout } from '@hooks/useAutoLayout';
import { useKeyboardShortcuts } from '@hooks/useKeyboardShortcuts';
import { ExecutionService } from '@services/executionService';
import { FlowService } from '@services/flowService';


interface FlowCanvasProps {
  onNodeSelect: (nodeId: string | null) => void;
  onNodeCountChange: (count: number) => void;
  onEdgeCountChange: (count: number) => void;
  onZoomChange: (zoom: number) => void;
}

// ── Inner component (needs ReactFlow context) ──
function FlowCanvasInner({
  onNodeSelect,
  onNodeCountChange,
  onEdgeCountChange,
  onZoomChange,
}: FlowCanvasProps) {
  const wrapperRef = useRef<HTMLDivElement>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [isInstanceReady, setIsInstanceReady] = useState(false);
  const reactFlowInstance = useReactFlow();
  useAutoLayout();

  // Settings
  const snapToGrid = useSettingsStore((s) => s.snapToGrid);
  const snapGridSize = useSettingsStore((s) => s.snapGridSize);

  // Node store
  const nodes = useNodeStore((state) => state.nodes);
  const onNodesChange = useNodeStore((state) => state.onNodesChange);
  const addNode = useNodeStore((state) => state.addNode);

  // Edge store
  const edges = useEdgeStore((state) => state.edges);
  const onEdgesChange = useEdgeStore((state) => state.onEdgesChange);
  const addEdge = useEdgeStore((state) => state.addEdge);

  // Execution store
  const executionStatus = useExecutionStore((s) => s.status);

  // Report counts to parent
  onNodeCountChange(nodes.length);
  onEdgeCountChange(edges.length);

  // ── Keyboard Shortcuts ──
  useKeyboardShortcuts({
    onAutoLayout: async () => {
      // Auto-layout is handled by the parent App component
    },
    onSave: () => {
      FlowService.saveFlow('My Flow');
    },
    onRun: async () => {
      if (executionStatus === 'running' || executionStatus === 'paused') {
        await ExecutionService.stopExecution();
      } else {
        await ExecutionService.startExecution();
      }
    },
  });

  // ── Node Types ──
  // NOTE: getStepNodeTypes() is called here (lazy) to avoid circular dependency issues.
  // The schemas module imports node components which import stores which import back to schemas.
  // By deferring this computation to render time, all modules are fully initialized.
  const nodeTypes = useMemo(() => ({
    ...getStepNodeTypes(),
    // Category-level fallbacks
    ai: AiNode,
    rule: RuleNode,
    data: DataNode,
    api: ApiNode,
    transform: TransformNode,
    utility: UtilityNode,
    subflow: SubFlowNode,
    terminal: TerminalNode,
    flow: FlowNode,
    // Ultimate fallback for any unrecognized node type
    default: DefaultNode,
  }), []);

  const edgeTypes = useMemo(() => ({
    'step-edge': StepEdge,
  }), []);

  // ── Pass original nodes to ReactFlow ──
  // NOTE: We must NOT create new node objects here. ReactFlow v12 tracks nodes
  // by reference internally (for dragging, selection, etc.). Creating new objects
  // on every render breaks internal state tracking and makes nodes undraggable.
  // Execution state styling is handled inside the node components themselves.

  // ── Handle Connection ──
  const onConnect = useCallback(
    (params: Connection) => {
      const edge: Edge = {
        id: uuidv4(),
        ...params,
        type: 'step-edge',
        animated: true,
        style: { stroke: '#6366f1', strokeWidth: 2 },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: '#6366f1',
        },
      };
      addEdge(edge);
    },
    [addEdge]
  );

  // ── Handle Selection Change ──
  const onSelectionChange = useCallback(
    ({ nodes: selectedNodes }: { nodes: Node[]; edges: Edge[] }) => {
      const selected = selectedNodes.length > 0 ? selectedNodes[0].id : null;
      onNodeSelect(selected);
    },
    [onNodeSelect]
  );

  // ── Handle Zoom Change ──
  const onMoveEnd = useCallback(
    (_: unknown, viewport: { zoom: number }) => {
      onZoomChange(viewport.zoom);
    },
    [onZoomChange]
  );


  return (
    <div
      ref={wrapperRef}
      className="w-full h-full relative"
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onSelectionChange={onSelectionChange}
        onNodeClick={(_, node: Node) => {
          onNodeSelect(node.id);
        }}
        onMoveEnd={onMoveEnd}
        onInit={() => setIsInstanceReady(true)}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        snapToGrid={snapToGrid}
        snapGrid={[snapGridSize, snapGridSize]}
        defaultViewport={{ x: 0, y: 0, zoom: 1 }}
        className="bg-canvas-bg"
        minZoom={0.1}
        maxZoom={2}
        onDragOver={(event: React.DragEvent) => {
          event.preventDefault();
          event.dataTransfer.dropEffect = 'move';
          setIsDragOver(true);
        }}
        onDragLeave={() => {
          setIsDragOver(false);
        }}
        onDrop={(event: React.DragEvent) => {
          setIsDragOver(false);

          if (!isInstanceReady) return;

          const schemaId = event.dataTransfer.getData('application/stepflow-schema');
          if (!schemaId) return;

          // Only prevent default when we're actually dropping a palette item
          event.preventDefault();

          const position = reactFlowInstance.screenToFlowPosition({
            x: event.clientX,
            y: event.clientY,
          });

          addNode(schemaId, position);
        }}
        onNodeContextMenu={(_event: React.MouseEvent, node: Node) => {
          // Select the node to open its properties panel
          useNodeStore.getState().setSelectedNode(node.id);
          onNodeSelect(node.id);
        }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={20}
          size={1}
          bgColor="var(--bg-canvas)"
          color="#2a2a4a"
        />
        <MiniMap
          pannable
          zoomable
          nodeStrokeWidth={3}
          maskColor="rgba(10, 10, 26, 0.9)"
          nodeColor={(node) => {
            if (node.data?.color) return (node.data.color as string) || '#6366f1';
            return '#6366f1';
          }}
        />
        <Controls
          showZoom={true}
          showFitView={true}
          showInteractive={true}
        />
      </ReactFlow>

      {/* Drag overlay indicator */}
      {isDragOver && (
        <div className="absolute inset-0 bg-indigo-500/10 border-2 border-dashed border-indigo-500/50 pointer-events-none z-50 flex items-center justify-center">
          <div className="text-sm text-indigo-400 font-medium">
            Drop to add step
          </div>
        </div>
      )}

      {executionStatus === 'running' && (
        <div className="absolute top-2 right-2 z-50 bg-amber-500/20 border border-amber-500/30 text-amber-400 text-xs px-3 py-1.5 rounded-lg flex items-center gap-2">
          <div className="w-2 h-2 bg-amber-400 rounded-full animate-pulse" />
          Executing...
        </div>
      )}

      {/* Empty canvas welcome message */}
      {nodes.length === 0 && executionStatus !== 'running' && (
        <div className="absolute inset-0 pointer-events-none flex items-center justify-center z-[1]">
          <div className="text-center max-w-md mx-auto px-4">
            <div className="text-6xl mb-4">⚡</div>
            <h3 className="text-xl font-semibold text-gray-300 mb-2">
              Build Your Workflow
            </h3>
            <p className="text-sm text-gray-500 mb-3 leading-relaxed">
              Drag states from the palette to start building your flow.
            </p>
            <div className="flex items-center justify-center gap-4 text-xs text-gray-600">
              <span className="flex items-center gap-1">
                <span className="w-2 h-2 rounded-full bg-emerald-500 inline-block" />
                Start with <strong className="text-emerald-400">START</strong>
              </span>
              <span className="text-gray-700">→</span>
              <span className="flex items-center gap-1">
                <span className="w-2 h-2 rounded-full bg-gray-500 inline-block" />
                Add states
              </span>
              <span className="text-gray-700">→</span>
              <span className="flex items-center gap-1">
                <span className="w-2 h-2 rounded-full bg-rose-500 inline-block" />
                End with <strong className="text-rose-400">END</strong>
              </span>
            </div>
            <div className="mt-4 text-xs text-gray-600">
              Double-click palette items for quick add
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Wrapper with ReactFlowProvider ──
export function FlowCanvas(props: FlowCanvasProps) {
  return (
    <ReactFlowProvider>
      <FlowCanvasInner {...props} />
    </ReactFlowProvider>
  );
}
