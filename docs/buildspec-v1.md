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
7. [Reusable Step Library](#reusable-step-library)
8. [Sub-Flow Calling](#sub-flow-calling)
9. [Custom Node Components Per Category](#custom-node-components-per-category)
10. [Component Architecture](#component-architecture)
11. [State Management](#state-management)
12. [Backend Integration Plan](#backend-integration-plan)
13. [UI/UX Design](#uiux-design)
14. [Implementation Phases](#implementation-phases)
15. [Open Questions](#open-questions)

---

## Executive Summary

We are replacing the current div-based drag-and-drop UI in `StepFunctionsApp/` with a professional node-based flow builder inspired by **chaiNNer**'s architecture, using **@xyflow/react** (React Flow v12) as the canvas engine.

**What stays**: The .NET 10 backend (`StepFunctionService`, `StepFunctionInterpreter`, `ResourceInvoker`, all execution engines) is production-quality and will be kept as-is.

**What changes**: Everything in `StepFunctionsApp/src/`, `StepFunctionsApp/StepUI/`, and the frontend build pipeline.

**Key architectural pillars**:

1. **Custom Node Per Category** — Each `StepCategory` (AI, Rule, Data, API, Transform, Utility) gets its own dedicated React node component with category-specific rendering, handles, and visual language. No generic "one-size-fits-all" node.

2. **Reusable Step Library** — Steps are defined as composable schemas that can be saved as templates, shared across flows, and versioned. A step used in one flow can be referenced in another without duplication.

3. **Sub-Flow Calling** — Flows can invoke other flows as first-class nodes (`SubFlowNode`), enabling hierarchical workflow composition and DRY step orchestration.

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
│   │   │   ├── BaseNode.tsx           ← Shared foundation (handles, drag, selection)
│   │   │   │
│   │   │   ├── AiNode.tsx             ← Custom node for AI category
│   │   │   │   ├── AiNodeHeader.tsx   ← Model badge, temp indicator
│   │   │   │   ├── AiNodeBody.tsx     ← Prompt preview, service icon
│   │   │   │   └── AiNodeFooter.tsx   ← Output format badge
│   │   │   │
│   │   │   ├── RuleNode.tsx           ← Custom node for Rule category
│   │   │   │   ├── RuleNodeHeader.tsx ← Rule count badge
│   │   │   │   ├── RuleNodeBody.tsx   ← Condition preview
│   │   │   │   └── RuleNodeFooter.tsx ← Default outcome
│   │   │   │
│   │   │   ├── DataNode.tsx           ← Custom node for Data category
│   │   │   │   ├── DataNodeHeader.tsx ← DB icon, type badge
│   │   │   │   ├── DataNodeBody.tsx   ← Query preview
│   │   │   │   └── DataNodeFooter.tsx ← Row count
│   │   │   │
│   │   │   ├── ApiNode.tsx            ← Custom node for API category
│   │   │   │   ├── ApiNodeHeader.tsx  ← Method badge (GET/POST)
│   │   │   │   ├── ApiNodeBody.tsx    ← Endpoint preview
│   │   │   │   └── ApiNodeFooter.tsx  ← Status code
│   │   │   │
│   │   │   ├── TransformNode.tsx      ← Custom node for Transform category
│   │   │   │   ├── TransformHeader.tsx ← Language badge
│   │   │   │   ├── TransformBody.tsx   ← Expression preview
│   │   │   │   └── TransformFooter.tsx ← Processing time
│   │   │   │
│   │   │   ├── UtilityNode.tsx        ← Custom node for Utility category
│   │   │   │   ├── UtilityHeader.tsx  ← Simple icon
│   │   │   │   ├── UtilityBody.tsx    ← Pass-through indicator
│   │   │   │   └── UtilityFooter.tsx  ← Minimal
│   │   │   │
│   │   │   ├── SubFlowNode.tsx        ← Custom node for Sub-Flow calls
│   │   │   │   ├── SubFlowHeader.tsx  ← Target flow name, version
│   │   │   │   ├── SubFlowBody.tsx    ← I/O mapping preview
│   │   │   │   └── SubFlowFooter.tsx  ← Test/Edit buttons
│   │   │   │
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
│   │   │   ├── FavoriteNodes.tsx      ← Favorites list
│   │   │   └── LibraryPanel.tsx       ← Reusable step library browser
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
│   │   ├── index.ts                ← Schema registry + nodeTypes map
│   │   ├── types.ts                ← Schema type definitions
│   │   ├── categories.ts           ← Category definitions + component map
│   │   └── steps/                  ← Individual step schemas
│   │       ├── ai.ts               ← AI Decision, AI Text
│   │       ├── rule.ts             ← Rule Engine, MS Rules
│   │       ├── data.ts             ← SQL, DuckDB, EAV
│   │       ├── api.ts              ← HTTP, Registered API
│   │       ├── transform.ts        ← JSONata, Script
│   │       ├── utility.ts          ← Pass, Wait, Branch
│   │       └── subflow.ts          ← Sub-Flow Call node
│   │
│   ├── library/                    ← Reusable step library
│   │   ├── StepLibraryStore.ts     ← Zustand store for library
│   │   ├── LibraryService.ts       ← CRUD for templates
│   │   └── templates/              ← Pre-built template definitions
│   │       ├── ai-templates.ts
│   │       ├── data-templates.ts
│   │       └── api-templates.ts
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
  category: StepCategory;    // Category for palette grouping + custom node selection
  description: string;       // Help text
  icon: string;              // Icon name (lucide icon or emoji)
  color: string;             // Accent color (hex)
  inputs: StepInput[];       // Input handles
  outputs: StepOutput[];     // Output handles
  configFields: ConfigField[]; // Property panel fields
  validation: ValidationRule[]; // Validation rules
  deprecated?: boolean;      // Hide from palette

  // ── Reusable Library ──
  version: string;           // "1.0.0" — semantic version for the step definition
  isTemplate: boolean;       // Can be saved as a reusable template
  templateId?: string;       // If this schema is based on a template
  tags: string[];            // For search and filtering in palette
  author?: string;           // Who created this step definition

  // ── Custom Node Component Mapping ──
  // Each category maps to a dedicated React node component.
  // The canvas uses this to render the correct visual for the step.
  nodeComponent: StepCategory; // e.g., "ai" → AiNode, "data" → DataNode
}

// ===== REUSABLE STEP LIBRARY ENTRY =====
export interface StepLibraryEntry {
  id: string;                // "lib:ai:credit-decision-v2"
  schemaId: string;          // References the base StepSchema
  name: string;              // "Credit Decision — v2"
  description: string;
  category: StepCategory;
  version: string;
  configuration: Record<string, unknown>; // Pre-filled defaults
  inputs: StepInput[];       // Can override base schema inputs
  outputs: StepOutput[];     // Can override base schema outputs
  tags: string[];
  createdAt: string;
  updatedAt: string;
  usageCount: number;        // How many flows reference this template
  isPublished: boolean;      // Available to all users vs private
}

// ===== CATEGORY =====
export type StepCategory = 'ai' | 'rule' | 'data' | 'api' | 'transform' | 'utility' | 'subflow';

export interface CategoryDefinition {
  id: StepCategory;
  name: string;              // "AI & LLM"
  description: string;
  icon: string;
  color: string;
  nodeComponent: string;     // React component name, e.g., "AiNode", "DataNode"
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

### Schema Registry + Custom Node Type Mapping

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

// ── Custom Node Components Per Category ──
// Each category gets its own React component. The canvas dispatches
// to the correct component based on schema.category (nodeComponent).
import { AiNode } from '../components/Nodes/AiNode';
import { RuleNode } from '../components/Nodes/RuleNode';
import { DataNode } from '../components/Nodes/DataNode';
import { ApiNode } from '../components/Nodes/ApiNode';
import { TransformNode } from '../components/Nodes/TransformNode';
import { UtilityNode } from '../components/Nodes/UtilityNode';
import { SubFlowNode } from '../components/Nodes/SubFlowNode';

export const categoryNodeComponents: Record<StepCategory, React.ComponentType<any>> = {
  ai: AiNode,
  rule: RuleNode,
  data: DataNode,
  api: ApiNode,
  transform: TransformNode,
  utility: UtilityNode,
  subflow: SubFlowNode,
};

// Build xyflow nodeTypes map: { "stepflow:ai:decision": AiNode, ... }
export const stepNodeTypes = Object.fromEntries(
  stepSchemas.map(schema => [
    schema.schemaId,
    categoryNodeComponents[schema.nodeComponent],
  ])
);

// ── Reusable Step Library ──
export const stepLibrary: StepLibraryEntry[] = [
  // Pre-built templates that users can drop onto the canvas
  {
    id: 'lib:ai:credit-decision-v1',
    schemaId: 'stepflow:ai:decision',
    name: 'Credit Decision — Standard',
    description: 'Evaluate creditworthiness using GPT-4',
    category: 'ai',
    version: '1.0.0',
    configuration: {
      model: 'gpt-4-turbo',
      prompt: 'Evaluate credit risk based on applicant data...',
      temperature: 0.3,
      llmService: 'azureOpenAI',
    },
    tags: ['credit', 'finance', 'ai'],
    usageCount: 0,
    isPublished: true,
  },
  // ... more templates
];

export const libraryById = new Map(stepLibrary.map(e => [e.id, e]));
export const libraryByCategory = new Map<StepCategory, StepLibraryEntry[]>();
```

---

---

## Reusable Step Library

### Concept
Steps are not just definitions — they are **composable, versioned, shareable templates**. A step configured once can be reused across dozens of flows without copy-pasting configuration.

### How It Works

```
┌──────────────────────────────────────────────────────────────────┐
│                        STEP LIBRARY                              │
│                                                                  │
│  ┌─────────────────────┐  ┌─────────────────────┐               │
│  │ lib:ai:credit-v1    │  │ lib:sql:customer-v2 │               │
│  │ ─────────────────── │  │ ─────────────────── │               │
│  │ Based on: AI Schema │  │ Based on: SQL Schema│               │
│  │ Pre-configured:     │  │ Pre-configured:     │               │
│  │  model: gpt-4       │  │  db: customers.db   │               │
│  │  prompt: "..."      │  │  query: "SELECT..." │               │
│  │  temp: 0.3          │  │  params: {...}      │               │
│  │                     │  │                     │               │
│  │ Usage: 14 flows     │  │ Usage: 8 flows      │               │
│  │ Published ✓         │  │ Published ✓         │               │
│  └─────────────────────┘  └─────────────────────┘               │
│                                                                  │
│  When dropped on canvas → creates a node with pre-filled config  │
│  Changes to template DO NOT affect existing instances (fork)     │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Library Operations

```typescript
// services/stepLibraryService.ts

interface StepLibraryService {
  // List all available templates
  listTemplates(category?: StepCategory, tags?: string[]): Promise<StepLibraryEntry[]>;

  // Get a template by ID
  getTemplate(id: string): Promise<StepLibraryEntry>;

  // Save current node config as a new template
  saveAsTemplate(nodeId: string, name: string, tags: string[]): Promise<StepLibraryEntry>;

  // Update an existing template (creates new version)
  updateTemplate(id: string, updates: Partial<StepLibraryEntry>): Promise<StepLibraryEntry>;

  // Publish/unpublish a template
  setPublished(id: string, published: boolean): Promise<void>;

  // Create a node from a template (fork — independent instance)
  instantiateTemplate(templateId: string, position: XYPosition): StepNode;
}
```

### Template Instance Lifecycle

```
Template (lib:ai:credit-v1)
    │
    ├─→ Flow A: "Order Processing" ──→ node_ai_1 (forked, independent)
    │                                    ├─ config can be modified
    │                                    └─ changes DON'T affect template
    │
    ├─→ Flow B: "Loan Approval" ──→  node_ai_5 (forked, independent)
    │
    └─→ Template Updated to v2
            │
            └─→ New instances use v2 config
                Existing instances remain on v1 (forked)
```

### Storage

```
StepFunctionsApp/
├── StepLibrary/                    ← New directory
│   ├── templates.json              ← All library entries
│   └── categories/                 ← Per-category template groups
│       ├── ai-templates.json
│       ├── rule-templates.json
│       ├── data-templates.json
│       └── api-templates.json
```

---

## Sub-Flow Calling

### Concept
Flows can invoke **other flows** as first-class nodes. This enables hierarchical workflow composition — a complex flow can be broken into reusable sub-flows, each with its own canvas, validation, and execution.

### SubFlowNode

```
┌─────────────────────────────────────┐
│  📦 Sub-Flow: Customer Validation   │  ← SubFlowNode header
│  ─────────────────────────────────  │
│  Flow: "customer-validate-v2"       │  ← Target flow reference
│  Version: 1.2.0                     │
│                                     │
│  Inputs:                            │
│  ┌────────┐  ┌────────┐            │
│  │orderId │  │custId  │ ◀─ handles │
│  └────────┘  └────────┘            │
│                                     │
│  Outputs:                           │
│  ┌──────────┐  ┌────────┐          │
│  │isValid   │─▶ │result │ ▶─ handles│
│  └──────────┘  └────────┘          │
│                                     │
│  [▶ Test] [📝 Edit Sub-Flow]        │
└─────────────────────────────────────┘
```

### SubFlow Schema Definition

```typescript
export const subFlowSchema: StepSchema = {
  schemaId: 'stepflow:subflow:invoke',
  name: 'Sub-Flow Call',
  category: 'subflow',
  description: 'Invoke another StepFlow as a sub-process.',
  icon: 'git-fork',
  color: '#06b6d4',  // Cyan — distinct from all other categories
  nodeComponent: 'subflow',

  inputs: [
    { id: 'input_payload', label: 'Input Payload', type: 'json', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', position: 'right' },
    { id: 'output_status', label: 'Status', type: 'string', position: 'right' },
  ],

  configFields: [
    {
      id: 'targetFlowId',
      label: 'Target Flow',
      type: 'dropdown',
      required: true,
      // Options populated dynamically from registered flows
      options: [], // Filled at runtime from StepFunctionService.ListStateMachines()
    },
    {
      id: 'inputMapping',
      label: 'Input Mapping',
      type: 'json',
      default: {},
      description: 'Map parent flow outputs to sub-flow inputs',
    },
    {
      id: 'outputMapping',
      label: 'Output Mapping',
      type: 'json',
      default: {},
      description: 'Map sub-flow outputs back to parent',
    },
    {
      id: 'timeout',
      label: 'Timeout (seconds)',
      type: 'number',
      default: 300,
      min: 10,
      max: 3600,
    },
    {
      id: 'onFailure',
      label: 'On Failure',
      type: 'dropdown',
      default: 'propagate',
      options: [
        { label: 'Propagate Error', value: 'propagate' },
        { label: 'Continue with Default', value: 'continue' },
        { label: 'Retry', value: 'retry' },
      ],
    },
    {
      id: 'retryCount',
      label: 'Retry Count',
      type: 'number',
      default: 0,
      condition: (data) => data.configuration?.onFailure === 'retry',
    },
  ],

  validation: [
    {
      id: 'target_flow_required',
      check: (data) => ({
        isValid: !!data.configuration?.targetFlowId,
        reason: 'Target flow must be selected',
      }),
    },
  ],
};
```

### Sub-Flow Execution Flow

```
Parent Flow Execution
    │
    ├─→ Node 1: AI Decision ──→ complete
    ├─→ Node 2: SQL Query ──→ complete
    │
    ├─→ Node 3: SubFlowNode ──→ invokes "customer-validate" flow
    │       │
    │       ├─→ Sub-Flow starts (new execution context)
    │       │   ├─→ sub-node-1: Rule Check
    │       │   ├─→ sub-node-2: SQL Lookup
    │       │   └─→ sub-node-3: AI Decision
    │       │
    │       ├─→ Sub-Flow completes → returns result
    │       └─→ Result mapped back to parent
    │
    ├─→ Node 4: API Call (uses sub-flow result)
    └─→ Node 5: Pass (final output)
```

### Backend Integration (already supported)

The existing `ResourceInvoker.HandleFlowAsync` already supports sub-flow calls:

```csharp
// Existing code in ResourceInvoker.cs — no changes needed
if (resource.StartsWith("flow://"))
    return await HandleFlowAsync(resource, input, ct);

private async Task<JToken> HandleFlowAsync(string resource, JToken input, CancellationToken ct)
{
    var flowId = resource["flow://".Length..].Trim('/');
    var execution = await _stepService.Value.ExecuteSyncAsync(flowId, input, ct);
    if (execution.Status == ExecutionStatus.Failed)
        throw new StepEngineException(execution.ErrorCode ?? "SubFlow.Failed", execution.ErrorMessage ?? "Sub-flow failed");
    return execution.Output;
}
```

The SubFlowNode maps to `flow://<flowId>` resource URI — the backend already handles this.

---

## Custom Node Components Per Category

### Concept
Each `StepCategory` has its **own dedicated React component** for rendering nodes on the canvas. This means AI nodes look different from Data nodes, which look different from API nodes — each with category-appropriate visuals, handles, and information density.

### Why Not One Generic Node?

| Generic Node | Custom Per-Category Node |
|---|---|
| Same visual for all types | Each category has distinct visual language |
| Hard to extend per type | Easy to add category-specific features |
| chaiNNer uses this too | chaiNNer has `NoteNode`, special nodes, etc. |
| Configuration buried in panel | Key info visible directly on node |

### Component Hierarchy

```
components/Nodes/
├── BaseNode.tsx              ← Shared base (handles, drag, selection, context menu)
│
├── AiNode.tsx                ← AI category nodes
│   ├── AiNodeHeader.tsx      ← Model badge, temperature indicator
│   ├── AiNodeBody.tsx        ← Prompt preview, service icon
│   └── AiNodeFooter.tsx      ← Output format badge
│
├── RuleNode.tsx              ← Rule category nodes
│   ├── RuleNodeHeader.tsx    ← Rule count badge
│   ├── RuleNodeBody.tsx      ← Condition preview
│   └── RuleNodeFooter.tsx    ← Default outcome indicator
│
├── DataNode.tsx              ← Data category nodes (SQL, DuckDB, EAV)
│   ├── DataNodeHeader.tsx    ← Database icon, type badge
│   ├── DataNodeBody.tsx      ← Query preview (truncated)
│   └── DataNodeFooter.tsx    ← Row count estimate
│
├── ApiNode.tsx               ← API category nodes
│   ├── ApiNodeHeader.tsx     ← Method badge (GET/POST/etc)
│   ├── ApiNodeBody.tsx       ← Endpoint preview
│   └── ApiNodeFooter.tsx     ← Status code (when executed)
│
├── TransformNode.tsx         ← Transform category nodes
│   ├── TransformNodeHeader.tsx ← Language badge
│   ├── TransformNodeBody.tsx   ← Expression preview
│   └── TransformNodeFooter.tsx ← Processing time
│
├── UtilityNode.tsx           ← Utility category nodes
│   ├── UtilityNodeHeader.tsx  ← Simple icon
│   ├── UtilityNodeBody.tsx    ← Pass-through indicator
│   └── UtilityNodeFooter.tsx  ← Minimal
│
└── SubFlowNode.tsx           ← Sub-flow invocation nodes
    ├── SubFlowNodeHeader.tsx  ← Target flow name, version
    ├── SubFlowNodeBody.tsx    ← Input/output mapping preview
    └── SubFlowNodeFooter.tsx  ← Execution status, timeout
```

### BaseNode (Shared Foundation)

Every category node extends `BaseNode` which provides:

```typescript
interface BaseNodeProps {
  id: string;
  data: StepNodeData;
  selected: boolean;
  isConnecting: boolean;

  // Inherited from xyflow
  isDraggable: boolean;
  isSelectable: boolean;

  // Validation
  validity: Validity;

  // Execution state
  executionStatus: NodeExecutionStatus;
  executionProgress?: number;

  // Handles
  inputs: StepInput[];
  outputs: StepOutput[];
}

function BaseNode({ id, data, selected, validity, executionStatus, inputs, outputs }: BaseNodeProps) {
  return (
    <div className="step-node" style={{ borderColor: getBorderColor(validity, selected, executionStatus) }}>
      {/* Input handles — left side */}
      {inputs.map(input => (
        <Handle
          key={input.id}
          type="target"
          position={Position.Left}
          id={input.id}
          style={{ background: dataTypeColor(input.type) }}
        />
      ))}

      {/* Category-specific content — rendered by child */}
      <slot />

      {/* Output handles — right side */}
      {outputs.map(output => (
        <Handle
          key={output.id}
          type="source"
          position={Position.Right}
          id={output.id}
          style={{ background: dataTypeColor(output.type) }}
        />
      ))}

      {/* Context menu trigger */}
      <ContextMenu onActions={getNodeContextMenuActions(data)} />
    </div>
  );
}
```

### Example: AiNode (Custom Per-Category Rendering)

```typescript
function AiNode({ data, selected, validity, executionStatus }: NodeProps) {
  const config = data.configuration as AIStepConfig;
  const schema = schemaById.get(data.schemaId);

  return (
    <BaseNode
      data={data}
      selected={selected}
      validity={validity}
      executionStatus={executionStatus}
      inputs={schema.inputs}
      outputs={schema.outputs}
    >
      {/* ── AI-Specific Header ── */}
      <div className="ai-node-header" style={{ background: schema.color }}>
        <BrainIcon className="w-4 h-4" />
        <span className="font-semibold">{data.nodeName || schema.name}</span>
        <Badge variant="secondary">{config.model}</Badge>
        {executionStatus === 'running' && <Spinner className="w-3 h-3" />}
      </div>

      {/* ── AI-Specific Body ── */}
      <div className="ai-node-body">
        <div className="truncate text-xs opacity-70">
          {config.prompt.substring(0, 60)}...
        </div>
        <div className="flex gap-2 mt-1">
          <span className="text-xs px-1.5 py-0.5 rounded bg-purple-900/30">
            temp: {config.temperature ?? 0.7}
          </span>
          <span className="text-xs px-1.5 py-0.5 rounded bg-purple-900/30">
            {config.llmService}
          </span>
        </div>
      </div>

      {/* ── AI-Specific Footer ── */}
      <div className="ai-node-footer">
        <span className="text-xs opacity-50">{config.outputFormat}</span>
        <button onClick={() => propertyPanel.open(data.id)}>⚙️</button>
      </div>
    </BaseNode>
  );
}
```

### Example: SubFlowNode (Distinct Visual for Flow Invocation)

```typescript
function SubFlowNode({ data, selected, validity, executionStatus }: NodeProps) {
  const config = data.configuration as SubFlowConfig;
  const targetFlow = flowStore.getFlow(config.targetFlowId);

  return (
    <BaseNode
      data={data}
      selected={selected}
      validity={validity}
      executionStatus={executionStatus}
      inputs={schema.inputs}
      outputs={schema.outputs}
    >
      {/* ── Sub-Flow Header ── */}
      <div className="subflow-node-header" style={{ background: '#06b6d4' }}>
        <GitForkIcon className="w-4 h-4" />
        <span className="font-semibold">Sub-Flow</span>
        <Badge variant="outline">v{targetFlow?.version ?? '?'}</Badge>
      </div>

      {/* ── Sub-Flow Body ── */}
      <div className="subflow-node-body">
        <div className="text-sm font-medium">{targetFlow?.name || config.targetFlowId}</div>
        <div className="text-xs opacity-60 mt-0.5">
          {targetFlow?.description || 'No description'}
        </div>

        {/* Input/Output mapping preview */}
        <div className="mt-2 space-y-1">
          <div className="text-xs flex items-center gap-1">
            <ArrowInIcon className="w-3 h-3 text-green-400" />
            <span className="opacity-70">{Object.keys(config.inputMapping ?? {}).length} inputs mapped</span>
          </div>
          <div className="text-xs flex items-center gap-1">
            <ArrowOutIcon className="w-3 h-3 text-blue-400" />
            <span className="opacity-70">{Object.keys(config.outputMapping ?? {}).length} outputs mapped</span>
          </div>
        </div>
      </div>

      {/* ── Sub-Flow Footer ── */}
      <div className="subflow-node-footer flex gap-2">
        <button
          className="text-xs px-2 py-1 rounded bg-cyan-900/30 hover:bg-cyan-900/50"
          onClick={() => testSubFlow(data.id)}
        >
          ▶ Test
        </button>
        <button
          className="text-xs px-2 py-1 rounded bg-cyan-900/30 hover:bg-cyan-900/50"
          onClick={() => openSubFlowEditor(config.targetFlowId)}
        >
          📝 Edit
        </button>
        <button onClick={() => propertyPanel.open(data.id)}>⚙️</button>
      </div>
    </BaseNode>
  );
}
```

### Canvas Node Type Registration

```typescript
// The canvas automatically resolves the correct component per schema
const nodeTypes: NodeTypes = {
  // Each schemaId maps to its category's custom component
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'ai')
      .map(s => [s.schemaId, AiNode])
  ),
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'rule')
      .map(s => [s.schemaId, RuleNode])
  ),
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'data')
      .map(s => [s.schemaId, DataNode])
  ),
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'api')
      .map(s => [s.schemaId, ApiNode])
  ),
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'transform')
      .map(s => [s.schemaId, TransformNode])
  ),
  ...Object.fromEntries(
    stepSchemas
      .filter(s => s.category === 'utility')
      .map(s => [s.schemaId, UtilityNode])
  ),
  // Sub-flow is its own category
  'stepflow:subflow:invoke': SubFlowNode,
};
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

### API Endpoints (existing .NET backend — minimal additions)

#### Existing Endpoints (no changes needed)
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

#### New Endpoints (small additions)
| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/library/templates` | List reusable step templates |
| GET | `/api/library/templates/:id` | Get template definition |
| POST | `/api/library/templates` | Save node config as template |
| PUT | `/api/library/templates/:id` | Update template (new version) |
| DELETE | `/api/library/templates/:id` | Delete template |
| POST | `/api/library/templates/:id/instantiate` | Create node from template |
| GET | `/api/flows/:id/subflows` | List flows callable as sub-flows |
| POST | `/api/flows/test-subflow` | Test sub-flow connection |

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
- [ ] Set up project structure (including new Nodes/ and library/ dirs)
- [ ] Configure Vite, TypeScript, Tailwind
- [ ] Verify xyflow renders on canvas
- [ ] Basic 3-panel layout (palette | canvas | properties)

### Phase 2: Node Schema System + Category Definitions (Week 1-2)
- [ ] Define core types (StepSchema, StepInput, StepOutput, ConfigField)
- [ ] Define StepLibraryEntry type + StepLibraryService interface
- [ ] Create schema registry with `categoryNodeComponents` map
- [ ] Define all 10+ step schemas + SubFlow schema
- [ ] Define 7 category definitions (ai, rule, data, api, transform, utility, subflow)
- [ ] Build `stepNodeTypes` map (schemaId → category component)

### Phase 2.5: Reusable Step Library Foundation (Week 2)
- [ ] Create `library/StepLibraryStore.ts` (Zustand store)
- [ ] Create `library/LibraryService.ts` (CRUD operations)
- [ ] Define initial template set (ai-templates, data-templates, api-templates)
- [ ] Build `LibraryPanel.tsx` for browsing templates in palette
- [ ] Implement template → node instantiation (fork, not link)
- [ ] Add "Save as Template" button in property panel

### Phase 3: BaseNode + Custom Nodes Per Category (Week 2-4)
- [ ] Build `BaseNode.tsx` (shared handles, drag, selection, context menu, validity)
- [ ] Build `AiNode.tsx` with header/body/footer sub-components
- [ ] Build `RuleNode.tsx` with header/body/footer sub-components
- [ ] Build `DataNode.tsx` with header/body/footer sub-components
- [ ] Build `ApiNode.tsx` with header/body/footer sub-components
- [ ] Build `TransformNode.tsx` with header/body/footer sub-components
- [ ] Build `UtilityNode.tsx` with header/body/footer sub-components
- [ ] Build `SubFlowNode.tsx` with target flow selector, I/O mapping preview, test/edit buttons
- [ ] Register all node types in canvas `nodeTypes` map
- [ ] Verify each category renders distinctly on canvas

### Phase 3.5: Sub-Flow Calling (Week 3-4)
- [ ] Implement SubFlowNode component with flow dropdown (populated from registered flows)
- [ ] Build input/output mapping UI in property panel
- [ ] Implement sub-flow test execution (▶ Test button)
- [ ] Implement sub-flow editor launch (📝 Edit button → opens target flow in new tab/panel)
- [ ] Map SubFlowNode → `flow://<flowId>` resource URI for backend
- [ ] Handle sub-flow execution states (running, completed, failed, timeout)
- [ ] Add retry configuration for sub-flow failures
- [ ] Visual feedback: sub-flow progress shown on SubFlowNode during execution

### Phase 4: Core Canvas + Node Palette (Week 3-4)
- [ ] FlowCanvas component with xyflow
- [ ] Custom StepEdge with animations
- [ ] Grid, minimap, controls
- [ ] Category accordion in palette
- [ ] Search with fuzzy matching
- [ ] Favorites system
- [ ] Drag-to-canvas (both from schema and from library templates)
- [ ] Collapse/expand panel

### Phase 5: Property Panel (Week 4-5)
- [ ] Dynamic form from schema
- [ ] Conditional field visibility
- [ ] Real-time validation
- [ ] Tabbed interface (Basic / Advanced / Connections / Template)
- [ ] Code editor for scripts/prompts
- [ ] "Save as Template" in Template tab
- [ ] "Forked from Template" badge on property panel

### Phase 6: Validation System (Week 5)
- [ ] Real-time validity checks
- [ ] Visual feedback (border colors)
- [ ] Error tooltips
- [ ] Type compatibility checking
- [ ] Sub-flow target validation (flow exists, is runnable)

### Phase 7: Execution & Polish (Week 5-6)
- [ ] Auto-layout with ELKJS
- [ ] Backend API integration (including new library + sub-flow endpoints)
- [ ] Flow save/load
- [ ] Execution progress visualization (including sub-flow nesting)
- [ ] Undo/redo
- [ ] Keyboard shortcuts
- [ ] Theme toggle
- [ ] Export/import

### Phase 8: Testing & Migration (Week 6-7)
- [ ] Unit tests for all 7 category node components
- [ ] Unit tests for library service (template CRUD, instantiation)
- [ ] Unit tests for sub-flow calling (execution, mapping, error handling)
- [ ] Integration tests for canvas interactions
- [ ] E2E tests for complete flow creation
- [ ] Migrate existing flows from `Flows/` directory
- [ ] Create initial template library from existing flow patterns
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

### Core Canvas
1. ✅ All 10+ step types render as proper nodes on the canvas
2. ✅ Each StepCategory renders with its own custom node component (7 distinct visuals)
3. ✅ Nodes can be connected with typed edges
4. ✅ Node palette with search, categories, and library panel
5. ✅ Property panel with dynamic forms and template tab
6. ✅ Real-time validation with visual feedback
7. ✅ Auto-layout working

### Reusable Step Library
8. ✅ Templates can be created from any configured node
9. ✅ Templates can be instantiated onto canvas (forked, not linked)
10. ✅ Template versions are tracked and independent
11. ✅ Library browser in palette with category/tag filtering

### Sub-Flow Calling
12. ✅ SubFlowNode renders with target flow info and I/O mapping
13. ✅ Sub-flows execute correctly via existing `flow://` resource scheme
14. ✅ Sub-flow test execution works from node footer
15. ✅ Sub-flow error handling (propagate, continue, retry)
16. ✅ Nested sub-flow execution (flow → sub-flow → sub-sub-flow)

### Integration
17. ✅ Flows can be saved/loaded/executed via existing backend
18. ✅ Existing flows from `Flows/` directory load correctly
19. ✅ Keyboard shortcuts working
20. ✅ Dark/light theme toggle

---

*This buildspec is a living document. Update it as we make decisions during implementation.*
