# StepFlow Builder — Build Specification

> **Goal**: Replace the clunky UI of `StepFunctionsApp` with an advanced node-based flow builder inspired by [chaiNNer](https://github.com/chaiNNer-org/chaiNNer), built on [xyflow (React Flow v12)](https://github.com/xyflow/xyflow).

---

## Table of Contents

1. [chaiNNer Analysis](#chainner-analysis)
2. [xyflow Foundation](#xyflow-foundation)
3. [Architecture](#architecture)
4. [Phase 1 — Foundation](#phase-1--foundation)
5. [Phase 2 — Custom Nodes](#phase-2--custom-nodes)
6. [Phase 3 — Advanced Features](#phase-3--advanced-features)
7. [Phase 4 — Polish & Production](#phase-4--polish--production)
8. [Technical Decisions](#technical-decisions)
9. [Open Questions](#open-questions)

---

## chaiNNer Analysis

### What chaiNNer Does Well

chaiNNer is a powerful visual programming tool for AI/ML workflows. Key strengths:

| Feature | Description | Relevance to StepFlow |
|---|---|---|
| **Node-based Graph Editor** | Drag-and-drop nodes with typed ports and connections | Core foundation — already implemented via xyflow |
| **Schema Registry** | Central registry of all available nodes with metadata | Implemented (`src/schemas/index.ts`) |
| **Typed Data Flow** | Each port has a type; connections validated at edit time | Implemented via `DataType` enum + `StepInput`/`StepOutput` |
| **Category System** | Nodes grouped by domain (AI, Data, API, etc.) | Implemented with 7 categories |
| **Node Configuration** | Per-node property panels for editing parameters | Partially done — `NodeDetailPanel` needs upgrade |
| **Sub-Flow Support** | Nodes can encapsulate nested graphs | Planned — `SubFlowNode` component created |
| **Blender-like UI** | Professional dark theme with gradient accents | Partially done — CSS variables in `index.css` |
| **Custom Node Rendering** | Each category has distinct visual styling | Implemented — 7 category-specific node components |
| **Pipeline Execution** | DAG-based execution with progress tracking | Planned — Phase 3 |
| **Blueprint System** | Save/load reusable node configurations | Planned — Phase 3 |

### What We Adapt (and What We Skip)

**Adopting:**
- Node palette with category tabs and search
- Schema-driven node generation
- Typed port connections with visual type indicators
- Dark theme with category color accents
- Node collapse/expand behavior
- Sub-flow nesting capability
- Blueprint/template system

**Skipping (not relevant to Step Functions):**
- chaiNNer's image/video processing pipeline
- chaiNNer's native Rust/C++ backend
- chaiNNer's file system integration
- chaiNNer's model download/management

---

## xyflow Foundation

### Why xyflow (React Flow v12)

xyflow is the modern successor to React Flow, providing:

| Feature | Benefit |
|---|---|
| **React Server Components ready** | Future-proof architecture |
| **Improved performance** | Virtual scrolling, better change detection |
| **Built-in mini-map, controls, background** | Less custom code |
| **Custom node/edge types** | Category-specific node rendering |
| **Edge validation** | Prevent invalid connections |
| **Viewport management** | Zoom, pan, fit-to-content |
| **D3-force layout** | Auto-arrange nodes |

### Current xyflow Integration

```
src/components/Canvas/
├── FlowCanvas.tsx        # Main canvas with ReactFlow
├── StepEdge.tsx          # Custom edge with category colors
├── FlowToolbar.tsx       # Zoom, fit, grid toggles
└── FlowStatusBar.tsx     # Node/edge counts, zoom level

src/stores/
├── useNodeStore.ts       # Zustand store for nodes
└── useEdgeStore.ts       # Zustand store for edges

src/schemas/
├── index.ts              # Schema registry + node component map
├── categories.ts         # 7 category definitions
└── steps/                # Schema definitions per category
    ├── ai.ts
    ├── rule.ts
    ├── data.ts
    ├── api.ts
    ├── transform.ts
    ├── utility.ts
    └── subflow.ts
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         StepFlow Builder                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────────┐  ┌──────────────────────┐  ┌───────────────────┐ │
│  │  NodePalette │  │   FlowCanvas (xyflow) │  │ NodeDetailPanel  │ │
│  │              │  │                      │  │                   │ │
│  │ • Category   │  │ • Custom Nodes       │  │ • Schema-driven   │ │
│  │   Tabs       │  │ • Category Nodes     │  │   form fields     │ │
│  │ • Search     │  │ • StepEdge           │  │ • Validation      │ │
│  │ • Drag       │  │ • MiniMap            │  │ • Live preview    │ │
│  │   Support    │  │ • Controls           │  │                   │ │
│  └──────────────┘  └──────────────────────┘  └───────────────────┘ │
│                              │                                      │
│  ┌───────────────────────────┴───────────────────────────────────┐  │
│  │                    Schema Registry                             │  │
│  │                                                                │  │
│  │  • StepSchema definitions (7 categories × 12+ steps)          │  │
│  │  • Node component mapping (category → React component)        │  │
│  │  • Palette data generation                                    │  │
│  │  • Search & filter                                            │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    Zustand Stores                              │  │
│  │                                                                │  │
│  │  • useNodeStore  — CRUD for flow nodes                         │  │
│  │  • useEdgeStore  — CRUD for flow edges                         │  │
│  │  • useFlowStore  — Flow metadata, execution state              │  │
│  │  • useBlueprintStore — Saved templates                         │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1 — Foundation ✅ (COMPLETE)

### Completed

- [x] Schema types defined (`src/schema-types/schema.ts`)
- [x] Schema registry with 7 categories and 12+ step schemas
- [x] Zustand stores for nodes and edges
- [x] FlowCanvas with xyflow integration
- [x] NodePalette with category tabs and search
- [x] Drag-and-drop node creation
- [x] Connection management (edges)
- [x] Dark theme CSS variables
- [x] TypeScript compilation passing

### Files

```
src/
├── schema-types/
│   └── schema.ts              # Core type definitions
├── schemas/
│   ├── index.ts               # Registry + component map
│   ├── categories.ts          # 7 category definitions
│   └── steps/
│       ├── ai.ts              # LLM, text generation schemas
│       ├── rule.ts            # RulesEngine, MS RulesEngine schemas
│       ├── data.ts            # SQL, EAV, CosmosDB schemas
│       ├── api.ts             # REST API, registered API schemas
│       ├── transform.ts       # Script, JSONata schemas
│       ├── utility.ts         # Pass, wait, branch schemas
│       └── subflow.ts         # Sub-flow invocation schema
├── stores/
│   ├── useNodeStore.ts        # Node CRUD
│   └── useEdgeStore.ts        # Edge CRUD
├── components/
│   ├── Canvas/
│   │   ├── FlowCanvas.tsx     # Main canvas
│   │   ├── StepEdge.tsx       # Custom edge
│   │   ├── FlowToolbar.tsx    # Controls
│   │   └── FlowStatusBar.tsx  # Status bar
│   ├── Nodes/
│   │   ├── BaseNode.tsx       # Shared node base
│   │   ├── AiNode.tsx         # AI category nodes
│   │   ├── RuleNode.tsx       # Rule category nodes
│   │   ├── DataNode.tsx       # Data category nodes
│   │   ├── ApiNode.tsx        # API category nodes
│   │   ├── TransformNode.tsx  # Transform category nodes
│   │   ├── UtilityNode.tsx    # Utility category nodes
│   │   └── SubFlowNode.tsx    # Sub-flow category nodes
│   └── Palette/
│       └── NodePalette.tsx    # Node palette with categories
└── App.tsx                    # Main app layout
```

---

## Phase 2 — Custom Nodes ✅ (COMPLETE)

### Completed

- [x] BaseNode with collapse/expand, handles, gradient headers
- [x] 7 category-specific node components with distinct styling
- [x] Typed input/output handles with visual type indicators
- [x] Config badges showing key parameters
- [x] Node collapse mode (compact header-only view)
- [x] Schema registry wired to node components
- [x] StepEdge with category-aware coloring
- [x] Node palette drag-and-drop with schema registration

### Node Visual Design

| Category | Color | Icon | Badge |
|---|---|---|---|
| AI | Purple (#8B5CF6) | 🧠 Brain | Model name, temperature |
| Rule | Amber (#F59E0B) | ⚖️ Scale | Rule count, mode |
| Data | Blue (#3B82F6) | 🗄️ Database | Table name, query preview |
| API | Green (#10B981) | 🔌 Plug | HTTP method, URL |
| Transform | Pink (#EC4899) | 💻 Code | Language, expression |
| Utility | Gray (#6B7280) | ↗️ Arrow | Duration, mode |
| Sub-Flow | Cyan (#06B6D4) | 📂 Folder | Target flow, version |

---

## Phase 3 — Advanced Features

### 3.1 Enhanced Node Detail Panel

The current `NodeDetailPanel` needs a complete overhaul to match chaiNNer's property panel:

- [ ] **Schema-driven form generation** — Each schema defines its configuration fields; the panel renders them dynamically
- [ ] **Field validators** — Per-field validation with inline error display
- [ ] **Live preview** — JSON preview of the node's configuration
- [ ] **Connection inspector** — Show which nodes are connected to this node's ports
- [ ] **Execution history** — Show last execution result for the node
- [ ] **Node metadata** — Version, author, tags displayed in panel
- [ ] **Tabs within panel** — Configuration / Connections / History / Info tabs

### 3.2 Blueprint System

- [ ] **Save flow as blueprint** — Export current flow configuration
- [ ] **Load blueprint** — Import and instantiate saved flows
- [ ] **Blueprint library** — Pre-configured templates (e.g., "AI + Rule pipeline")
- [ ] **Blueprint sharing** — JSON export/import with versioning
- [ ] **Partial blueprints** — Save node groups, not entire flows

### 3.3 Execution Engine Integration

- [ ] **Flow execution trigger** — Button to run the flow
- [ ] **Node-by-node execution** — Visual progress through the DAG
- [ ] **Execution status indicators** — Running, success, error per node
- [ ] **Execution logs** — Console-like output panel
- [ ] **Error highlighting** — Failed nodes glow red with error tooltip
- [ ] **Debug mode** — Step-through execution with data inspection

### 3.4 Sub-Flow Editor

- [ ] **Double-click to edit sub-flow** — Open nested flow editor
- [ ] **Breadcrumb navigation** — Show current nesting level
- [ ] **Context passing** — Define what data flows in/out of sub-flow
- [ ] **Sub-flow versioning** — Track changes to nested flows

### 3.5 Advanced Graph Features

- [ ] **Auto-layout** — D3 force-directed or layered layout
- [ ] **Node grouping** — Visually group related nodes
- [ ] **Edge labels** — Show data type on connections
- [ ] **Edge validation** — Prevent invalid type connections
- [ ] **Undo/Redo** — History stack for all graph changes
- [ ] **Copy/Paste nodes** — Clipboard support
- [ ] **Keyboard shortcuts** — Delete, select all, zoom, etc.

---

## Phase 4 — Polish & Production

### 4.1 Performance

- [ ] **Virtual scrolling** for large flows (100+ nodes)
- [ ] **Lazy node rendering** — Only render visible nodes
- [ ] **Edge caching** — Memoize edge paths
- [ ] **Debounced store updates** — Batch node position changes

### 4.2 Accessibility

- [ ] **Keyboard navigation** — Tab between nodes, arrow keys to move
- [ ] **Screen reader support** — ARIA labels on nodes and handles
- [ ] **Focus indicators** — Visible focus rings
- [ ] **Color contrast** — Ensure WCAG AA compliance

### 4.3 Theming

- [ ] **Light theme** — Mirror dark theme for light mode
- [ ] **Custom accent colors** — Allow user to customize category colors
- [ ] **Font size scaling** — Accessibility zoom support

### 4.4 Testing

- [ ] **Unit tests** — Schema registry, stores, utilities
- [ ] **Component tests** — Node rendering, palette interactions
- [ ] **Integration tests** — Full flow creation and editing
- [ ] **E2E tests** — Playwright tests for critical user journeys

### 4.5 Documentation

- [ ] **Schema authoring guide** — How to add new step schemas
- [ ] **Node component guide** — How to create custom node visuals
- [ ] **Architecture doc** — System design decisions
- [ ] **API reference** — Store APIs, schema APIs

---

## Technical Decisions

### 1. xyflow over React Flow

**Decision**: Use xyflow (React Flow v12) as the base.

**Rationale**:
- React Flow v12 is now branded as xyflow
- Better performance with virtual scrolling
- Improved TypeScript types
- Active development, React Server Components ready
- Drop-in replacement for React Flow v11

### 2. Zustand over Redux

**Decision**: Use Zustand for state management.

**Rationale**:
- Minimal boilerplate
- Built-in TypeScript support
- Selective re-renders via selectors
- No provider wrapping needed
- Already integrated in the codebase

### 3. Schema-Driven Node Generation

**Decision**: Define nodes via schema objects, not hardcoded components.

**Rationale**:
- Adding new node types is a JSON/schema change, not code
- Palette auto-generates from schemas
- Node components are mapped by category, not per-node
- chaiNNer's approach proven at scale (100+ node types)

### 4. Category-Based Visual Styling

**Decision**: 7 categories, each with distinct color/icon/badge.

**Rationale**:
- Users can instantly identify node purpose by color
- Matches chaiNNer's visual language
- Reduces cognitive load in complex flows
- Extensible — new categories just add new colors

### 5. TypeScript-First

**Decision**: Strict TypeScript with no `any` types.

**Rationale**:
- Type-safe node configurations
- Compile-time validation of schema definitions
- Better IDE autocomplete
- Easier refactoring

---

## Open Questions

### 1. Backend Integration Strategy

**Question**: How does the flow builder communicate with the existing Step Functions backend?

**Options:**
- A) REST API — Flow JSON sent to `/api/flows` endpoint
- B) WebSocket — Real-time sync during editing
- C) Hybrid — REST for save/load, WebSocket for collaboration

**Recommendation**: Start with (A), add (C) if multi-user editing needed.

### 2. Node Configuration Persistence

**Question**: Where are node configurations stored?

**Options:**
- A) In the flow JSON (self-contained)
- B) In a separate configuration store (referenced by ID)
- C) Hybrid — defaults in flow, overrides in store

**Recommendation**: (A) for simplicity; node configs are part of the flow definition.

### 3. Execution Model

**Question**: How is the flow executed?

**Options:**
- A) Server-side — Flow JSON sent to backend for execution
- B) Client-side — JavaScript execution in browser (limited)
- C) Hybrid — Client validates, server executes

**Recommendation**: (A) — Step Functions nodes hit AWS/SQL/external APIs; must be server-side.

### 4. Collaboration

**Question**: Do we need real-time multi-user editing?

**Options:**
- A) No — Single user per flow
- B) Yes — CRDT-based conflict resolution
- C) Yes — Lock-based (one editor at a time)

**Recommendation**: Start with (A), evaluate (C) if team editing needed.

---

## Reference Links

| Resource | URL |
|---|---|
| chaiNNer GitHub | https://github.com/chaiNNer-org/chaiNNer |
| xyflow (React Flow v12) | https://github.com/xyflow/xyflow |
| xyflow Docs | https://xyflow.com/docs |
| Zustand | https://github.com/pmndrs/zustand |
| Tailwind CSS | https://tailwindcss.com |
| Lucide Icons | https://lucide.dev |
| Step Functions App (current) | `C:\Source\stepflow-builder\StepFunctionsApp` |

---

## Getting Started

```bash
cd C:\Source\stepflow-builder\StepFunctionsApp
npm install
npm run dev
```

Open http://localhost:3001 in your browser.

---

*Last updated: 2025-07-20*
*Status: Phase 1 & 2 Complete — Ready for Phase 3*
