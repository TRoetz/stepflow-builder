import { useCallback, useRef, useMemo, useState } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  MarkerType,
  ConnectionLineType,
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
  const setSelectedEdge = useEdgeStore((state) => state.setSelectedEdge);

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
      // Handled at App header level / global shortcut
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
  // NOTE: `...params` intentionally preserves sourceHandle/targetHandle so edges
  // render at the exact ports the user connected (important for multi-port nodes).
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

  // ── Handle Connection End (drop-on-node-body fallback) ──
  // React Flow only completes a connection when the pointer is released within
  // `connectionRadius` of a target handle dot. Users frequently aim at the node
  // card (or its port label), so in that case we resolve the node under the
  // pointer and wire it to the appropriate default port ourselves.
  //
  // This ref marks an *edge reconnection* drag in progress. React Flow fires
  // onConnectEnd for those drags too — before onReconnectEnd — so without this
  // guard, dropping a reconnect onto a node body would leave the original edge
  // intact AND create a duplicate one.
  const reconnectingEdgeRef = useRef(false);

  const onConnectEnd = useCallback(
    (
      event: MouseEvent | TouchEvent,
      connectionState: {
        isValid?: boolean | null;
        fromNode?: { id: string } | null;
        fromHandle?: { id?: string | null; type?: 'source' | 'target' } | null;
      }
    ) => {
      // A valid drop already created an edge via onConnect.
      if (connectionState?.isValid === true) return;

      // Ignore drops that end an edge reconnection: the original edge is kept,
      // so creating another one here would duplicate it.
      if (reconnectingEdgeRef.current) return;

      const clientX =
        event instanceof MouseEvent ? event.clientX : event.changedTouches[0]?.clientX;
      const clientY =
        event instanceof MouseEvent ? event.clientY : event.changedTouches[0]?.clientY;
      if (clientX == null || clientY == null) return;

      // Find the node under the pointer, skipping any overlay elements above it.
      const targetEl: HTMLElement | undefined = document
        .elementsFromPoint(clientX, clientY)
        .map((el) => el.closest?.('.react-flow__node'))
        .find(Boolean) as HTMLElement | undefined;
      if (!targetEl) return;

      const droppedNodeId = targetEl.getAttribute('data-id');
      const fromNodeId = connectionState.fromNode?.id;
      const fromHandle = connectionState.fromHandle;
      if (!droppedNodeId || !fromNodeId || fromNodeId === droppedNodeId) return;

      let source: string;
      let target: string;
      let sourceHandleId: string | null;
      let targetHandleId: string | null;

      if (fromHandle?.type !== 'target') {
        // Dragged from an output port → connect it to the dropped node's first input port.
        source = fromNodeId;
        target = droppedNodeId;
        sourceHandleId = fromHandle?.id ?? null;
        const inputEl = targetEl.querySelector('.react-flow__handle.react-flow__target');
        if (!inputEl) return; // Node has no input port (e.g. End).
        targetHandleId = inputEl.getAttribute('data-handleid');
      } else {
        // Dragged from an input port → connect the dropped node's first output port to it.
        source = droppedNodeId;
        target = fromNodeId;
        targetHandleId = fromHandle?.id ?? null;
        const outputEl = targetEl.querySelector('.react-flow__handle.react-flow__source');
        if (!outputEl) return; // Node has no output port (e.g. Start).
        sourceHandleId = outputEl.getAttribute('data-handleid');
      }

      // data-handleid is absent for collapsed nodes' single unnamed handle;
      // leaving the edge's handle undefined then matches that handle correctly.

      const { edges: currentEdges, addEdge } = useEdgeStore.getState();
      const duplicate = currentEdges.some(
        (e) =>
          e.source === source &&
          e.target === target &&
          (e.sourceHandle ?? null) === (sourceHandleId ?? null) &&
          (e.targetHandle ?? null) === (targetHandleId ?? null)
      );
      if (duplicate) return;

      addEdge({
        id: uuidv4(),
        source,
        target,
        sourceHandle: sourceHandleId ?? undefined,
        targetHandle: targetHandleId ?? undefined,
        type: 'step-edge',
        animated: true,
        style: { stroke: '#6366f1', strokeWidth: 2 },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: '#6366f1',
        },
      });
    },
    []
  );

  // ── Handle Selection Change ──
  const onSelectionChange = useCallback(
    ({ nodes: selectedNodes, edges: selectedEdges }: { nodes: Node[]; edges: Edge[] }) => {
      const selected = selectedNodes.length > 0 ? selectedNodes[0].id : null;
      onNodeSelect(selected);
      // Deselect edge if no edges are selected
      if (selectedEdges.length === 0) {
        setSelectedEdge(null);
      }
    },
    [onNodeSelect, setSelectedEdge]
  );

  // ── Handle Zoom Change ──
  const onMoveEnd = useCallback(
    (_: unknown, viewport: { zoom: number }) => {
      onZoomChange(viewport.zoom);
    },
    [onZoomChange]
  );

  // ── Handle Edge Click ──
  const onEdgeClick = useCallback(
    (_: unknown, edge: Edge) => {
      setSelectedEdge(edge.id);
    },
    [setSelectedEdge]
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
        onConnectEnd={onConnectEnd}
        onReconnectStart={() => {
          reconnectingEdgeRef.current = true;
        }}
        onReconnectEnd={() => {
          // Fires immediately after the same pointer-up's onConnectEnd.
          reconnectingEdgeRef.current = false;
        }}
        connectionRadius={30}
        connectionLineType={ConnectionLineType.SmoothStep}
        connectionLineStyle={{ stroke: '#6366f1', strokeWidth: 2 }}
        onSelectionChange={onSelectionChange}
        onEdgeClick={onEdgeClick}
        onNodeClick={(_, node: Node) => {
          onNodeSelect(node.id);
          setSelectedEdge(null);
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
