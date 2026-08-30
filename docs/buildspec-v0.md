# StepFlow Builder — Build Specification

> **Status**: Draft — under review
> **Based on**: chaiNNer architecture analysis + xyflow v12 (React Flow 12)
> **Target**: Replace the clunky div-based UI in `StepFunctionsApp/` with a professional node-based flow builder

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current State Analysis](#current-state-analysis)
3. [Reference Architecture: chaiNNer](#reference-architecture-chainner)
4. [Target Architecture](#target-architecture)
5. [Technology Stack](#technology-stack)
6. [Node Schema System](#node-schema-system)
7. [Component Architecture](#component-architecture)
8. [State Management](#state-management)
9. [Backend Integration Plan](#backend-integration-plan)
10. [UI/UX Design](#uiux-design)
11. [Implementation Phases](#implementation-phases)
12. [Open Questions](#open-questions)

---

## Executive Summary

We are replacing the current div-based drag-and-drop UI in `StepFunctionsApp/` with a professional node-based flow builder inspired by **chaiNNer**'s architecture, using **@xyflow/react** (React Flow v12) as the canvas engine.

**What stays**: The .NET 10 backend (`StepFunctionService`, `StepFunctionInterpreter`, `ResourceInvoker`, all execution engines) is production-quality and will be kept as-is.

**What changes**: Everything in `StepFunctionsApp/src/`, `StepFunctionsApp/StepUI/`, and the frontend build pipeline.

**Why chaiNNer as reference**: It has the most mature open-source node editor architecture — typed node schemas, conditional input groups, real-time validity checking, a searchable node palette, auto-layout, and execution visualization. All of these patterns map directly to our step-based workflow needs.

---

## Current State Analysis

### What Works Well (KEEP)
| Component | Location | Assessment |
|-----------|----------|------------|
| StepFunctionInterpreter | `StepFunctions/StepFunctionInterpreter.cs` | Amazon States Language compatible, handles Task/Choice/Pass/Wait/Parallel/Map |
| ResourceInvoker | `StepFunctions/ResourceInvoker.cs` | Clean scheme-based routing: `ai://`, `rule://`, `transform://`, `flow://`, `http://` |
| Step Types | `StepUI/Types/stepConfig.ts` | 10 types well-defined: AI, RULE, SQL, API, PASS, SCRIPT, HTTP, DUCKDB, JSONAT, EAV |
| Backend Services | `StepFunctions/*.cs` | DuckDB, RulesEngine, JSONata, AI Decision — all working |
| Flow Storage | `Flows/*.json` | JSON-based, loadable at startup |
| BPMN Converter | `StepFunctions/BpmnConverter.cs` | Converts BPMN XML to state machines |

### What Needs Replacement (DISCARD)
| Component | Issue |
|-----------|-------|
| `src/Components/StepBuilder/StepBuilderCanvas.tsx` | Manual div-based drag-and-drop, no proper node editor |
| `src/Components/StepBuilder/StepNode.tsx` | Basic styled div, no handles, no proper connections |
| `src/Components/StepBuilder/StepConnections.tsx` | SVG lines drawn manually, no edge routing |
| `StepUI/Components/StepConfig/` | Modal-based config, not integrated with canvas |
| Overall UX | No search, no categories, no validation, no auto-layout, no minimap |

---

## Reference Architecture: chaiNNer

### Key Patterns We'll Adopt

#### 1. Node Schema System (`src/common/common-types.ts`)
chaiNNer defines every node as a `NodeSchema` with typed inputs/outputs, conditional groups, and validation rules. We'll adapt this for our step types.

**chaiNNer pattern**:
```typescript
interface NodeSchema {
  schemaId: string;        // "chainner:utility:note"
  category: CategoryId;
  inputs: Input[];         // typed input handles
  outputs: Output[];       // typed output handles
  groupLayout: (InputId | Group)[];  // UI layout
  kind: NodeKind;          // regularNode | generator | collector | transformer
}
```

**Our adaptation**:
```typescript
interface StepSchema {
  schemaId: string;        // "stepflow:ai:decision"
  category: StepCategory;  // "ai" | "rule" | "data" | "api" | "transform" | "utility"
  inputs: StepInput[];     // typed input handles
  outputs: StepOutput[];   // typed output handles
  configFields: ConfigField[];  // property panel form fields
  validation: ValidationRule[];
}
```

#### 2. Node Selector Panel (`src/renderer/components/NodeSelectorPanel/`)
Searchable, category-based accordion with favorites, collapsible to icon-only mode.

**We'll adopt**: Category accordion, search bar, favorites, collapse/expand, drag-to-canvas.

#### 3. ReactFlowBox (`src/renderer/components/ReactFlowBox.tsx`)
Full-featured canvas with grid snap, minimap, controls, auto-layout (ELKJS), node-on-edge collision, export.

**We'll adopt**: Grid snap, minimap, controls, auto-layout, multi-select, keyboard shortcuts, export.

#### 4. Node Component (`src/renderer/components/node/Node.tsx`)
Header with accent color, body with handles, footer with config button, collapsed mode, disabled state, execution animation.

**We'll adopt**: Header/body/footer structure, accent colors, collapsed mode, execution animation, context menus.

#### 5. Validity System (`src/common/nodes/checkNodeValidity.ts`)
Real-time validation checking required inputs, type compatibility, and conditional groups.

**We'll adopt**: Real-time validation, visual feedback (border colors), tooltip errors.

#### 6. Context-Based State (`src/renderer/contexts/`)
React contexts for global state: GlobalNodeState, BackendContext, ExecutionContext, SettingsContext.

**We'll adapt**: Use Zustand stores instead (lighter weight, same pattern).

---

## Target Architecture

### High-Level Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  TOOLBAR HEADER                                                              │
│  [Logo] [Flow Name]  [Run ▶] [Stop ⏹] [Save] [Undo] [Redo] [Layout] [⚙️]    │
├──────────────┬─────────────────────────────────────────────────┬─────────────┤
│              │                                                 │             │
│  NODE        │           CANVAS (xyflow)                       │  PROPERTY   │
│  PALETTE     │                                                 │  PANEL      │
│              │  ┌─────────┐     ┌─────────┐                   │             │
│  🔍 Search   │  │ AI      │────▶│ RULE    │                   │  ┌───────┐  │
│              │  │ Decision│     │ Engine  │                   │  │Config │  │
│  ⭐ Favs    │  └─────────┘     └─────────┘                   │  │Form   │  │
│              │                                                 │  └───────┘  │
│  🤖 AI ▼    │  ┌─────────┐     ┌─────────┐                   │             │
│   ├ Decision│  │ SQL     │────▶│ JSONata │                   │  ┌───────┐  │
│   └ Text    │  │ Query   │     │         │                   │  │Valid  │  │
│              │  └─────────┘     └─────────┘                   │  │ation  │  │
│  ⚖️ Rule ▼  │                                                 │  └───────┘  │
│   ├ Engine  │  [MiniMap]  [Controls]                           │             │
│   └ MS Rules│                                                 │  [Save]     │
│              │                                                 │  [Delete]   │
│  📊 Data ▼  │                                                 │             │
│   ├ SQL     │                                                 │             │
│   ├ DuckDB  │                                                 │             │
│   └ EAV     │                                                 │             │
│              │                                                 │             │
│  🔌 API ▼   │                                                 │             │
│   ├ HTTP    │                                                 │             │
│   └ Reg API │                                                 │             │
│              │                                                 │             │
│  ⚙️ Transform│                                                 │             │
│   ├ JSONata │                                                 │             │
│   └ Script  │                                                 │             │
│              │                                                 │             │
│  🔧 Utility ▼│                                                 │             │
│   ├ Pass    │                                                 │             │
│   ├ Wait    │                                                 │             │
│   └ Branch  │                                                 │             │
│              │                                                 │             │
├──────────────┴─────────────────────────────────────────────────┴─────────────┤
│  STATUS BAR: [Zoom: 100%] [Nodes: 12] [Edges: 10] [Valid ✓] [Execution: Idle]│
└──────────────────────────────────────────────────────────────────────────────┘
```

### Directory Structure

```
StepFunctionsApp/
├── StepFunctions/              ← KEEP AS-IS (backend)
├── Converters/                 ← KEEP AS-IS
├── Flows/                      ← KEEP AS-IS
├── src/                        ← COMPLETE REWRITE
│   ├── App.tsx                 ← Main app with 3-panel layout
│   ├── main.tsx                ← Entry point
│   │
│   ├── components/
│   │   ├── Canvas/
│   │   │   ├── FlowCanvas.tsx         ← xyflow wrapper (ReactFlowBox)
│   │   │   ├── CanvasControls.tsx     ← Zoom, fit, layout buttons
│   │   │   └── CanvasToolbar.tsx      ← Canvas-specific toolbar
│   │   │
│   │   ├── Nodes/
│   │   │   ├── StepNode.tsx           ← Main node component
│   │   │   ├── NodeHeader.tsx         ← Title bar with accent color
│   │   │   ├── NodeBody.tsx           ← Input/output handles
│   │   │   ├── NodeFooter.tsx         ← Config button, type badge
│   │   │   └── SpecialNodes/          ← Start, End, Event nodes
│   │   │
│   │   ├── Edges/
│   │   │   ├── StepEdge.tsx           ← Custom edge with animations
│   │   │   └── EdgeLabel.tsx          ← Data type labels
│   │   │
│   │   ├── Palette/
│   │   │   ├── NodePalette.tsx        ← Category browser (NodeSelectorPanel)
│   │   │   ├── PaletteSearch.tsx      ← Search bar
│   │   │   ├── CategoryAccordion.tsx  ← Expandable categories
│   │   │   └── FavoriteNodes.tsx      ← Favorites list
│   │   │
│   │   ├── Properties/
│   │   │   ├── PropertyPanel.tsx      ← Right-side config panel
│   │   │   ├── ConfigForm.tsx         ← Dynamic form from schema
│   │   │   ├── ValidationPanel.tsx    ← Validation status display
│   │   │   └── ConnectionsPanel.tsx   ← Show connected nodes
│   │   │
│   │   ├── Header/
│   │   │   ├── AppHeader.tsx          ← Main toolbar
│   │   │   ├── ExecutionControls.tsx  ← Run/Stop/Pause buttons
│   │   │   └── StatusBar.tsx          ← Bottom status bar
│   │   │
│   │   └── Modals/
│   │       ├── SettingsModal.tsx      ← App settings
│   │       ├── ImportExportModal.tsx  ← Flow import/export
│   │       └── ConfirmModal.tsx       ← Generic confirmation
│   │
│   ├── stores/                     ← Zustand state stores
│   │   ├── useNodeStore.ts          ← Nodes state
│   │   ├── useEdgeStore.ts          ← Edges state
│   │   ├── useViewportStore.ts      ← Zoom, pan, selection
│   │   ├── useExecutionStore.ts     ← Execution progress
│   │   ├── usePaletteStore.ts       ← Palette state
│   │   ├── useSettingsStore.ts      ← User preferences
│   │   └── useUndoRedoStore.ts      ← Undo/redo history
│   │
│   ├── schemas/                    ← Node schema definitions
│   │   ├── index.ts                ← Schema registry
│   │   ├── types.ts                ← Schema type definitions
│   │   ├── categories.ts           ← Category definitions
│   │   └── steps/                  ← Individual step schemas
│   │       ├── ai.ts               ← AI Decision, AI Text
│   │       ├── rule.ts             ← Rule Engine, MS Rules
│   │       ├── data.ts             ← SQL, DuckDB, EAV
│   │       ├── api.ts              ← HTTP, Registered API
│   │       ├── transform.ts        ← JSONata, Script
│   │       └── utility.ts          ← Pass, Wait, Branch
│   │
│   ├── hooks/                      ← Custom React hooks
│   │   ├── useNodeValidity.ts      ← Real-time validation
│   │   ├── useAutoLayout.ts        ← ELKJS auto-layout
│   │   ├── useKeyboardShortcuts.ts ← Keyboard shortcuts
│   │   ├── useDragDrop.ts          ← Drag from palette
│   │   ├── useExecutionProgress.ts ← Execution visualization
│   │   └── useFavorites.ts         ← Favorites persistence
│   │
│   ├── services/                   ← Backend API services
│   │   ├── flowService.ts          ← CRUD for flows
│   │   ├── executionService.ts     ← Start/stop executions
│   │   └── apiRegistryService.ts   ← API registry operations
│   │
│   ├── types/                      ← TypeScript types
│   │   ├── flow.ts                 ← Flow definition types
│   │   ├── node.ts                 ← Node data types
│   │   ├── edge.ts                 ← Edge data types
│   │   └── execution.ts            ← Execution result types
│   │
│   ├── utils/                      ← Utilities
│   │   ├── validation.ts           ← Validation helpers
│   │   ├── layout.ts               ← Layout utilities
│   │   ├── colors.ts               ← Category accent colors
│   │   └── id.ts                   ← ID generation
│   │
│   └── styles/                     ← Theming
│       ├── globals.css             ← Global styles
│       ├── variables.css           ← CSS custom properties
│       └── themes/                 ← Dark/light themes
│           ├── dark.css
│           └── light.css
│
├── package.json                    ← Updated dependencies
├── vite.config.ts                  ← Updated Vite config
├── tsconfig.json                   ← TypeScript config
├── tailwind.config.js              ← Tailwind CSS config (if using)
└── index.html                      ← Entry HTML
```

---

## Technology Stack

### Core (Non-Negotiable)
| Package | Version | Purpose |
|---------|---------|---------|
| `@xyflow/react` | ^12.0.0 | Node editor canvas (React Flow 12) |
| `react` | ^18.3.0 | UI framework |
| `react-dom` | ^18.3.0 | DOM rendering |
| `typescript` | ^5.5.0 | Type safety |
| `vite` | ^5.4.0 | Build tool |

### State Management
| Package | Version | Purpose |
|---------|---------|---------|
| `zustand` | ^4.5.0 | Global state (lightweight, no boilerplate) |
| `@tanstack/react-query` | ^5.0.0 | Server state / API caching |

### Auto-Layout
| Package | Version | Purpose |
|---------|---------|---------|
| `elkjs` | ^0.8.2 | Graph auto-layout (same as chaiNNer) |

### UI Components (Decision Point — see [Open Questions](#open-questions))
| Option A (Recommended) | Option B | Option C |
|------------------------|----------|----------|
| `shadcn/ui` + Tailwind | `@chakra-ui/react` | `@mui/material` |
| Zero runtime, copy-paste | Runtime dependency | Runtime dependency |
| Tailwind CSS needed | Works with SCSS | Works with SCSS |
| **We'll go with A** | chaiNNer's choice | Heaviest |

### Utilities
| Package | Version | Purpose |
|---------|---------|---------|
| `uuid` | ^9.0.0 | ID generation |
| `clsx` + `tailwind-merge` | latest | Conditional classes |
| `use-debounce` | ^10.0.0 | Debounced inputs |
| `cmdk` | ^1.0.0 | Command palette / search |
| `lucide-react` | ^0.400.0 | Icons |

### Code Editor (for script/prompt fields)
| Package | Version | Purpose |
|---------|---------|---------|
| `@monaco-editor/react` | ^4.6.0 | In-property-panel code editor |

### Testing
| Package | Version | Purpose |
|---------|---------|---------|
| `vitest` | ^1.6.0 | Unit tests |
| `@testing-library/react` | ^14.1.0 | Component tests |
| `msw` | ^2.3.0 | API mocking |

---

## Node Schema System

### Core Types

```typescript
// ===== DATA TYPES (for edge type-checking) =====
export type DataType =
  | 'json'        // Arbitrary JSON object
  | 'string'      // Text string
  | 'number'      // Numeric value
  | 'boolean'     // True/false
  | 'array'       // JSON array
  | 'image'       // Image data
  | 'any';        // Accepts any type

// ===== STEP INPUT (node handle — input side) =====
export interface StepInput {
  id: string;              // "input_data", "input_config"
  label: string;           // "Input Data", "Configuration"
  type: DataType;          // For type-checking connections
  optional: boolean;       // Can be unconnected
  description?: string;    // Tooltip text
  position: 'left' | 'top'; // Handle position
}

// ===== STEP OUTPUT (node handle — output side) =====
export interface StepOutput {
  id: string;
  label: string;
  type: DataType;
  description?: string;
  position: 'right' | 'bottom';
}

// ===== CONFIG FIELD (property panel form field) =====
export type ConfigFieldType =
  | 'text'            // Single line text
  | 'textarea'        // Multi-line text
  | 'number'          // Numeric input
  | 'dropdown'        // Select dropdown
  | 'toggle'          // Boolean toggle
  | 'code'            // Code editor (Monaco)
  | 'json'            // JSON editor
  | 'file'            // File picker
  | 'color'           // Color picker
  | 'slider'          // Range slider
  | 'api-selector';    // API registry picker

export interface ConfigField {
  id: string;
  label: string;
  type: ConfigFieldType;
  default?: any;
  options?: { label: string; value: any }[];  // For dropdown
  min?: number; max?: number;                  // For number/slider
  condition?: (nodeData: NodeData) => boolean; // Conditional visibility
  required?: boolean;
  description?: string;
  validate?: (value: any) => string | null;    // Custom validation
}

// ===== VALIDATION RULE =====
export interface ValidationRule {
  id: string;
  check: (nodeData: NodeData, connectedInputs: Set<string>) => Validity;
}

export interface Validity {
  isValid: boolean;
  reason?: string;
}

// ===== STEP SCHEMA (the master definition) =====
export interface StepSchema {
  schemaId: string;          // "stepflow:ai:decision"
  name: string;              // "AI Decision"
  category: StepCategory;    // Category for palette grouping
  description: string;       // Help text
  icon: string;              // Icon name (lucide icon or emoji)
  color: string;             // Accent color (hex)
  inputs: StepInput[];       // Input handles
  outputs: StepOutput[];     // Output handles
  configFields: ConfigField[]; // Property panel fields
  validation: ValidationRule[]; // Validation rules
  deprecated?: boolean;      // Hide from palette
}

// ===== CATEGORY =====
export type StepCategory = 'ai' | 'rule' | 'data' | 'api' | 'transform' | 'utility';

export interface CategoryDefinition {
  id: StepCategory;
  name: string;              // "AI & LLM"
  description: string;
  icon: string;
  color: string;
}
```

### Example Schema: AI Decision Step

```typescript
export const aiDecisionSchema: StepSchema = {
  schemaId: 'stepflow:ai:decision',
  name: 'AI Decision',
  category: 'ai',
  description: 'Use an AI/LLM model to make a decision based on input data.',
  icon: 'brain',
  color: '#8B5CF6',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', position: 'right' },
    { id: 'output_decision', label: 'Decision', type: 'boolean', position: 'right' },
  ],

  configFields: [
    {
      id: 'model',
      label: 'Model',
      type: 'dropdown',
      default: 'gpt-4-turbo',
      options: [
        { label: 'GPT-4 Turbo', value: 'gpt-4-turbo' },
        { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
        { label: 'Claude 3', value: 'claude-3' },
      ],
      required: true,
    },
    {
      id: 'prompt',
      label: 'Prompt',
      type: 'code',
      default: 'Analyze the input and provide a decision.',
      required: true,
    },
    {
      id: 'temperature',
      label: 'Temperature',
      type: 'slider',
      default: 0.7,
      min: 0,
      max: 2,
    },
    {
      id: 'llmService',
      label: 'LLM Service',
      type: 'dropdown',
      default: 'azureOpenAI',
      options: [
        { label: 'Azure OpenAI', value: 'azureOpenAI' },
        { label: 'OpenAI', value: 'openAI' },
        { label: 'Anthropic', value: 'anthropic' },
      ],
    },
    {
      id: 'outputFormat',
      label: 'Output Format',
      type: 'dropdown',
      default: 'json',
      options: [
        { label: 'JSON', value: 'json' },
        { label: 'Text', value: 'text' },
        { label: 'Markdown', value: 'markdown' },
      ],
    },
  ],

  validation: [
    {
      id: 'model_required',
      check: (data) => ({
        isValid: !!data.configuration?.model,
        reason: 'Model is required',
      }),
    },
    {
      id: 'prompt_required',
      check: (data) => ({
        isValid: !!data.configuration?.prompt,
        reason: 'Prompt is required',
      }),
    },
  ],
};
```

### Schema Registry

```typescript
// schemas/index.ts
import { aiDecisionSchema } from './steps/ai';
import { ruleEngineSchema } from './steps/rule';
// ... etc

export const stepSchemas: StepSchema[] = [
  aiDecisionSchema,
  ruleEngineSchema,
  // ... all 10+ schemas
];

export const schemaById = new Map(stepSchemas.map(s => [s.schemaId, s]));
export const schemasByCategory = new Map<StepCategory, StepSchema[]>();
```

---

## Component Architecture

### FlowCanvas (ReactFlowBox equivalent)

```typescript
interface FlowCanvasProps {
  wrapperRef: RefObject<HTMLDivElement>;
}

function FlowCanvas({ wrapperRef }: FlowCanvasProps) {
  const { nodes, onNodesChange } = useNodeStore();
  const { edges, onEdgesChange } = useEdgeStore();
  const { snapToGrid, snapGridSize } = useSettingsStore();

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      onConnect={onConnect}           // Create edge
      onDrop={onDrop}                 // Drop from palette
      onDragOver={onDragOver}
      nodeTypes={stepNodeTypes}       // Custom node components
      edgeTypes={stepEdgeTypes}       // Custom edge components
      snapToGrid={snapToGrid}
      snapGrid={[snapGridSize, snapGridSize]}
      fitView
    >
      <Background variant="dots" gap={16} size={1} />
      <MiniMap pannable zoomable />
      <Controls />
      <CanvasControls />              // Custom: layout, export buttons
    </ReactFlow>
  );
}
```

### StepNode (Node.tsx equivalent)

```typescript
function StepNode({ data, selected }: { data: StepNodeData; selected: boolean }) {
  const schema = schemaById.get(data.schemaId);
  const validity = useNodeValidity(data);
  const executionStatus = useExecutionStore.getState().getNodeStatus(data.id);

  return (
    <div className="step-node" style={{ borderColor: selected ? schema.color : undefined }}>
      <NodeHeader
        title={data.nodeName || schema.name}
        accentColor={schema.color}
        validity={validity}
        executionStatus={executionStatus}
      />
      <NodeBody
        inputs={schema.inputs}
        outputs={schema.outputs}
        validity={validity}
      />
      <NodeFooter
        schemaId={data.schemaId}
        onConfigure={() => propertyPanel.open(data.id)}
        onDelete={() => nodeStore.removeNode(data.id)}
        onDuplicate={() => nodeStore.duplicateNode(data.id)}
      />
    </div>
  );
}
```

---

## State Management

### Zustand Stores

```typescript
// useNodeStore.ts
interface NodeState {
  nodes: StepNode[];
  setNodes: (updater: Updater<StepNode>) => void;
  addNode: (schemaId: string, position: XYPosition) => void;
  removeNode: (id: string) => void;
  updateNode: (id: string, updates: Partial<StepNodeData>) => void;
  duplicateNode: (id: string) => void;
  onNodesChange: OnNodesChange;
}

// useEdgeStore.ts
interface EdgeState {
  edges: StepEdge[];
  setEdges: (updater: Updater<StepEdge>) => void;
  addEdge: (connection: Connection) => void;
  removeEdge: (id: string) => void;
  onEdgesChange: OnEdgesChange;
}

// useViewportStore.ts
interface ViewportState {
  zoom: number;
  position: XYPosition;
  selection: string[];
  setZoom: (zoom: number) => void;
  setSelected: (ids: string[]) => void;
}

// useExecutionStore.ts
interface ExecutionState {
  status: 'idle' | 'running' | 'paused' | 'completed' | 'failed';
  currentNodeId: string | null;
  completedNodes: Set<string>;
  failedNodes: Map<string, string>; // nodeId -> error
  startExecution: () => Promise<void>;
  stopExecution: () => void;
}

// useUndoRedoStore.ts
interface UndoRedoState {
  undo: () => void;
  redo: () => void;
  canUndo: boolean;
  canRedo: boolean;
  pushState: () => void;
}
```

---

## Backend Integration Plan

### API Endpoints (existing .NET backend — no changes needed)

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/flows` | List all registered flows |
| GET | `/api/flows/:id` | Get flow definition |
| POST | `/api/flows` | Save/register a new flow |
| PUT | `/api/flows/:id` | Update existing flow |
| DELETE | `/api/flows/:id` | Delete a flow |
| POST | `/api/executions/:flowId` | Start execution |
| GET | `/api/executions/:id` | Get execution status |
| POST | `/api/executions/:id/stop` | Stop execution |
| GET | `/api/registry/apis` | List registered APIs |

### Flow Data Format (JSON ↔ xyflow conversion)

```typescript
// xyflow format → Amazon States Language format (for saving)
function exportFlowToJson(nodes: StepNode[], edges: StepEdge[]): StateMachineDefinition {
  return {
    startAt: findStartNode(nodes).id,
    states: Object.fromEntries(
      nodes.map(node => [
        node.id,
        {
          type: mapNodeTypeToStateType(node.data.schemaId),
          resource: buildResourceUri(node.data),
          next: findNextNode(node, edges)?.id,
          parameters: node.data.configuration,
          comment: node.data.description,
        }
      ])
    ),
  };
}

// Amazon States Language format → xyflow format (for loading)
function importFlowFromJson(definition: StateMachineDefinition): { nodes: StepNode[]; edges: StepEdge[] } {
  // Convert states to nodes with positions
  // Convert next references to edges
  // Map resource URIs to schema IDs
}
```

---

## UI/UX Design

### Theme (Dark Mode Default — chaiNNer-style)

```css
:root {
  /* Background layers */
  --bg-primary: #1a1a2e;
  --bg-secondary: #16213e;
  --bg-tertiary: #0f3460;
  --bg-canvas: #0a0a1a;

  /* Text */
  --text-primary: #e0e0e0;
  --text-secondary: #a0a0a0;
  --text-muted: #606060;

  /* Node colors */
  --node-bg: #1e1e3a;
  --node-border: #3a3a5c;
  --node-border-selected: var(--accent-color);
  --node-border-invalid: #ef4444;
  --node-border-valid: #22c55e;
  --node-border-warning: #f59e0b;

  /* Category accent colors */
  --accent-ai: #8B5CF6;
  --accent-rule: #F59E0B;
  --accent-data: #3B82F6;
  --accent-api: #10B981;
  --accent-transform: #EC4899;
  --accent-utility: #6B7280;

  /* Edge colors */
  --edge-default: #6366f1;
  --edge-valid: #22c55e;
  --edge-invalid: #ef4444;
  --edge-executing: #f59e0b;

  /* Palette */
  --palette-bg: #16213e;
  --palette-hover: #1e3a5f;
  --palette-active: #2a4a7f;
}
```

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Delete` / `Backspace` | Delete selected nodes/edges |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl+S` | Save flow |
| `Ctrl+Shift+F` | Auto-layout |
| `Ctrl+A` | Select all |
| `Ctrl+C` / `Ctrl+V` | Copy / Paste nodes |
| `Ctrl+D` | Duplicate selected |
| `Escape` | Deselect all |
| `Space + drag` | Pan canvas |
| `1-9` | Zoom presets |
| `0` | Fit view |

---

## Implementation Phases

### Phase 1: Project Scaffolding (Week 1)
- [ ] Install all dependencies
- [ ] Set up project structure
- [ ] Configure Vite, TypeScript, Tailwind
- [ ] Verify xyflow renders on canvas
- [ ] Basic 3-panel layout (palette | canvas | properties)

### Phase 2: Node Schema System (Week 1-2)
- [ ] Define core types (StepSchema, StepInput, StepOutput, ConfigField)
- [ ] Create schema registry
- [ ] Define all 10+ step schemas
- [ ] Define category definitions with colors/icons

### Phase 3: Core Canvas (Week 2-3)
- [ ] FlowCanvas component with xyflow
- [ ] Custom StepNode with header/body/footer
- [ ] Custom StepEdge with animations
- [ ] Grid, minimap, controls
- [ ] Add/remove nodes and edges
- [ ] Multi-select, delete, duplicate

### Phase 4: Node Palette (Week 3)
- [ ] Category accordion
- [ ] Search with fuzzy matching
- [ ] Favorites system
- [ ] Drag-to-canvas
- [ ] Collapse/expand panel

### Phase 5: Property Panel (Week 3-4)
- [ ] Dynamic form from schema
- [ ] Conditional field visibility
- [ ] Real-time validation
- [ ] Tabbed interface
- [ ] Code editor for scripts/prompts

### Phase 6: Validation System (Week 4)
- [ ] Real-time validity checks
- [ ] Visual feedback (border colors)
- [ ] Error tooltips
- [ ] Type compatibility checking

### Phase 7: Execution & Polish (Week 4-5)
- [ ] Auto-layout with ELKJS
- [ ] Backend API integration
- [ ] Flow save/load
- [ ] Execution progress visualization
- [ ] Undo/redo
- [ ] Keyboard shortcuts
- [ ] Theme toggle
- [ ] Export/import

### Phase 8: Testing & Migration (Week 5-6)
- [ ] Unit tests
- [ ] Integration tests
- [ ] Migrate existing flows
- [ ] Documentation
- [ ] Performance optimization

---

## Open Questions

### 1. UI Component Library
**Decision**: We'll use **shadcn/ui** with Tailwind CSS. It gives us the flexibility of chaiNNer's Chakra UI without the runtime dependency, and Tailwind is well-suited for the custom theming we need.

**Alternative**: If we want faster development, `@chakra-ui/react` (chaiNNer's choice) works but adds ~200KB runtime.

### 2. State Management
**Decision**: **Zustand** — it's the lightweight equivalent of chaiNNer's `use-context-selector` pattern but with less boilerplate. No Redux needed.

### 3. Code Editor
**Decision**: **Monaco Editor** (`@monaco-editor/react`) for script and prompt fields. It's the VS Code editor — feature-rich and familiar.

**Alternative**: CodeMirror if Monaco is too heavy (~1MB).

### 4. Backend API Changes
**Decision**: **No changes to backend**. The existing .NET API endpoints are sufficient. We'll add a thin service layer in the frontend to map xyflow data ↔ Amazon States Language JSON.

### 5. Flow File Format
**Decision**: **Keep Amazon States Language JSON** as the storage format. The UI layer converts to/from xyflow node/edge format on load/save.

### 6. Electron vs Browser
**Decision**: **Browser-only** (served by .NET backend). chaiNNer uses Electron because it's a standalone desktop app. We already have a .NET web server — no need for Electron.

### 7. SSR/SSG
**Decision**: **No SSR needed**. This is a client-side SPA served from the .NET backend. Vite handles the build.

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| xyflow v12 API changes | Medium | v12 is stable; we're the first to use it but reactflow v11 API is nearly identical |
| Learning curve for team | Medium | chaiNNer source is well-documented reference |
| Performance with large flows | Low | xyflow handles 1000+ nodes; virtualization available |
| Backend API changes | Low | We're not touching the backend |
| Theme consistency | Low | CSS variables + Tailwind utility classes |

---

## Success Criteria

1. ✅ All 10+ step types render as proper nodes on the canvas
2. ✅ Nodes can be connected with typed edges
3. ✅ Node palette with search and categories
4. ✅ Property panel with dynamic forms
5. ✅ Real-time validation with visual feedback
6. ✅ Auto-layout working
7. ✅ Flows can be saved/loaded/executed via existing backend
8. ✅ Existing flows from `Flows/` directory load correctly
9. ✅ Keyboard shortcuts working
10. ✅ Dark/light theme toggle

---

*This buildspec is a living document. Update it as we make decisions during implementation.*
