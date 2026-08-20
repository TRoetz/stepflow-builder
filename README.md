# StepFlow Builder

A professional node-based flow builder for designing [Amazon Step Functions](https://aws.amazon.com/step-functions/) workflows. Built with **xyflow (React Flow v12)** and **Zustand**, inspired by [chaiNNer's](https://github.com/chaiNNer-org/chaiNNer) node editor architecture.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  TOOLBAR: [Logo] [Flow Name]  [Run ▶] [Save] [Undo] [Redo] [Layout] [⚙️]    │
├──────────────┬──────────────────────────────────────────────┬───────────────┤
│  NODE        │  ┌─────────┐     ┌─────────┐                │  PROPERTY     │
│  PALETTE     │  │ AI      │────▶│ RULE    │                │  PANEL        │
│              │  │ Decision│     │ Engine  │                │               │
│  🔍 Search   │  └─────────┘     └─────────┘                │  ┌─────────┐  │
│              │  ┌─────────┐     ┌─────────┐                │  │ Config  │  │
│  ⭐ Favs    │  │ SQL     │────▶│ JSONata │                │  │ Form    │  │
│              │  └─────────┘     └─────────┘                │  └─────────┘  │
│  🤖 AI ▼    │                                              │               │
│  ⚖️ Rule ▼  │  [MiniMap]  [Controls]                       │  ┌─────────┐  │
│  📊 Data ▼  │                                              │  │ Validation│ │
│  🔌 API ▼   │                                              │  └─────────┘  │
│  ⚙️ Transform│                                              │               │
│  🔧 Utility ▼│                                              │  [Save]       │
│  📦 SubFlow │                                              │  [Delete]     │
├──────────────┴──────────────────────────────────────────────┴───────────────┤
│  STATUS BAR: [Zoom: 100%] [Nodes: 12] [Edges: 10] [Valid ✓] [Idle]         │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Using the App](#using-the-app)
- [Schema System](#schema-schema)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Project Structure](#project-structure)
- [Testing](#testing)
- [State Management](#state-management)
- [Backend Integration](#backend-integration)
- [MCP Server](#mcp-server)
- [Flow Migration](#flow-migration)
- [Roadmap](#roadmap)

---

## Overview

StepFlow Builder replaces the original div-based drag-and-drop UI with a professional node editor featuring:

- **15 step schemas** across 7 categories (AI, Rule, Data, API, Transform, Utility, SubFlow)
- **Custom node components** per category — AI nodes look different from Data nodes, etc.
- **Real-time validation** with visual feedback (green/red borders)
- **Auto-layout** via ELKJS graph layout engine
- **Undo/Redo** with 50-entry history
- **Reusable step library** with 6 pre-built templates
- **Flow save/load** to localStorage (exportable as Amazon States Language JSON)
- **Execution visualization** with animated progress on nodes
- **Keyboard shortcuts** for power users

---

## Architecture

The frontend lives in the **`StepFlow-UI/`** subfolder of this repo (React + xyflow). The **.NET 10 backend** (`StepFunctions/`) is kept as-is — it provides the execution engine, resource invoker, and flow storage. Vite builds into `dist/`, which the backend serves at `/`.

stepflow-builder/
└── StepFunctionsApp/              ← Backend (.NET 10) + this README; serves built UI from dist/
    ├── StepFlow-UI/               ← Frontend (React + xyflow)
    │   ├── Canvas with custom nodes       ← Drag, drop, connect, layout
    │   ├── Property Panel                 ← Dynamic config forms
    │   ├── Palette with search            ← 15 step types, 7 categories
    │   └── Zustand stores                 ← State management
    ├── StepFunctionInterpreter        ← Amazon States Language
    ├── ResourceInvoker               ← ai://, rule://, sql://, etc.
    ├── DuckDB, RulesEngine, JSONata  ← Execution engines
    └── Flow storage (Flows/*.json)    ← JSON-based persistence

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **xyflow (React Flow v12)** | Industry-standard node editor, performant, well-maintained |
| **Zustand** | Lightweight state management, no boilerplate, perfect for canvas state |
| **Custom node per category** | AI nodes look different from SQL nodes — better UX than generic nodes |
| **ELKJS auto-layout** | Same engine as chaiNNer, handles complex graphs well |
| **Schema-driven UI** | Schemas define nodes, config forms, validation — no hardcoded UI |

---

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|:---:|
| **Canvas** | @xyflow/react (React Flow) | 12.4 |
| **UI** | React | 18.3 |
| **State** | Zustand | 5.0 |
| **Build** | Vite | 5.4 |
| **Language** | TypeScript | 5.5 |
| **Styling** | Tailwind CSS | 3.4 |
| **Layout** | ELKJS | 0.9 |
| **Icons** | Lucide React | 0.477 |
| **Code Editor** | Monaco Editor | 4.7 |
| **Testing** | Vitest + Testing Library | 2.1 |

---

## Prerequisites

- **Node.js** 18+ (20 LTS recommended)
- **npm** 9+ (or yarn/pnpm)
- **.NET 10 SDK** (for backend execution — optional for UI-only development)

---

## Getting Started

### 1. Install Dependencies

```bash
cd StepFunctionsApp/StepFlow-UI
npm install
```

### 2. Run Development Server

```bash
npm run dev
```

The app opens at `http://localhost:3001`. The dev server proxies `/api` to the .NET backend (port 5001) and supports hot module replacement (HMR). Alternatively, run `start.ps1` from `StepFunctionsApp/` to launch backend + frontend together.

### 3. Build for Production

```bash
npm run build
```

This runs TypeScript type-checking then Vite production build. Output goes to `../StepFunctionsApp/dist/`, which the .NET backend serves at `/`.

### 4. Preview Production Build

```bash
npm run preview
```

### 5. Run Tests

```bash
npm test              # watch mode
npx vitest run       # one-shot run
npx vitest run --reporter=verbose  # detailed output
```

### Full Command Reference

| Command | Description |
|:--------|:------------|
| `npm run dev` | Start dev server (Vite HMR) |
| `npm run build` | TypeScript check + Vite production build |
| `npm run preview` | Preview production build locally |
| `npm run lint` | Run ESLint |
| `npm test` | Run Vitest in watch mode |
| `npm run test:ui` | Run Vitest with UI reporter |

---

## Using the App

### Adding Nodes

1. Open the **Node Palette** (left panel) — if collapsed, click the palette toggle in the header
2. Browse categories or use the **search bar** to find a step type
3. **Click** a node to add it to the canvas center, or **drag** it to a specific position
4. Click ⭐ to mark nodes as favorites (they appear at the top of the palette)

### Connecting Nodes

1. Hover over a node's **output handle** (right side dot) — it will highlight
2. **Click and drag** to an **input handle** (left side dot) on another node
3. A connection line appears — nodes are now linked
4. Click an edge to select it, press `Delete` to remove it

### Configuring Nodes

1. **Click a node** on the canvas to select it
2. The **Property Panel** (right panel) opens with three tabs:
   - **Config** — Dynamic form fields defined by the node's schema (dropdowns, sliders, code editors, toggles)
   - **Info** — Node metadata (schema ID, category, color, creation time)
   - **Template** — Save current configuration as a reusable template
3. Changes are applied **immediately** — no "Save" button needed

### Running Flows

1. Click the **Run** button (▶) in the header
2. Nodes execute in order — watch the **animated progress** on each node
3. Click **Stop** (⏹) to halt execution
4. Execution status appears in the status bar

### Auto-Layout

1. Click the **Layout** button in the header (or press `Ctrl+Shift+F`)
2. ELKJS automatically arranges all nodes in a clean hierarchical layout
3. Connections are routed with minimal crossings

### Saving Flows

1. Click **Save** in the header (or press `Ctrl+S`)
2. The flow is saved to localStorage with the current name
3. Saved flows can be loaded from the flow list (header menu)

---

## Schema System

Every step type is defined by a **StepSchema** that controls how the node looks, what config fields it has, and how it validates. The schema system is the foundation of the entire app.

### Schema ID Format

```
stepflow:<category>:<step-type>
```

Examples: `stepflow:ai:decision`, `stepflow:data:sql`, `stepflow:utility:pass`

### Categories and Step Types

| Category | Color | Step Types |
|:---------|:------|:-----------|
| **AI** | Purple `#8B5CF6` | AI Decision, AI Text Generation |
| **Rule** | Orange `#F97316` | Rule Engine, MS Rules |
| **Data** | Blue `#3B82F6` | SQL Query, DuckDB, EAV |
| **API** | Green `#22C55E` | HTTP Request, Registered API |
| **Transform** | Yellow `#EAB308` | JSONata, Script |
| **Utility** | Gray `#6B7280` | Pass, Wait, Branch |
| **SubFlow** | Cyan `#06B6D4` | Sub-Flow Invoke |

### Schema Structure

Each schema defines:

```typescript
interface StepSchema {
  schemaId: string;          // "stepflow:ai:decision"
  name: string;              // "AI Decision"
  category: string;          // "ai"
  color: string;             // "#8B5CF6"
  inputs: StepInput[];       // Left-side connection handles
  outputs: StepOutput[];     // Right-side connection handles
  configFields: ConfigField[]; // Property panel form fields
  validation: ValidationRule[]; // Real-time validation rules
  tags: string[];            // Search keywords
  isTemplate: boolean;       // Can be saved as reusable template
}
```

### Config Field Types

| Type | UI Component | Example |
|:-----|:-------------|:--------|
| `text` | Text input | Model name |
| `textarea` | Multi-line text | Notes |
| `number` | Number input | Max tokens |
| `dropdown` | Select dropdown | Model selection |
| `toggle` | Boolean switch | Enable caching |
| `code` | Monaco editor | SQL query, prompt |
| `json` | JSON editor | Parameters |
| `slider` | Range slider | Temperature |
| `color` | Color picker | Node accent |
| `file` | File picker | Upload config |
| `api-selector` | API registry picker | Registered API |

---

## Keyboard Shortcuts

| Shortcut | Action |
|:---------|:-------|
| `Ctrl + Z` | Undo |
| `Ctrl + Y` | Redo |
| `Ctrl + S` | Save flow |
| `Ctrl + Enter` | Run / Stop execution |
| `Ctrl + Shift + F` | Auto-arrange nodes |
| `Delete` | Delete selected node(s) |
| `Escape` | Deselect all |

---

## Project Structure

```
stepflow-builder/
└── StepFunctionsApp/              ← Backend (.NET 10) + this README; serves built UI from dist/
    ├── StepFlow-UI/               ← Frontend (React + xyflow)
    │   ├── src/
    │   │   ├── App.tsx                    ← Main app (3-panel layout)
    │   │   ├── main.tsx                   ← Entry point
    │   │   │
    │   │   ├── components/
    │   │   │   ├── Canvas/
    │   │   │   │   ├── FlowCanvas.tsx     ← xyflow canvas wrapper
    │   │   │   │   └── StepEdge.tsx       ← Custom edge with animations
    │   │   │   ├── Header/
    │   │   │   │   ├── AppHeader.tsx      ← Toolbar with Run/Save/Layout
    │   │   │   │   └── StatusBar.tsx      ← Zoom, node count, execution status
    │   │   │   ├── Nodes/                 ← Custom node per category
    │   │   │   │   ├── BaseNode.tsx       ← Shared foundation (handles, drag)
    │   │   │   │   ├── AiNode.tsx         ← AI Decision, AI Text
    │   │   │   │   ├── RuleNode.tsx       ← Rule Engine, MS Rules
    │   │   │   │   ├── DataNode.tsx       ← SQL, DuckDB, EAV
    │   │   │   │   ├── ApiNode.tsx        ← HTTP, Registered API
    │   │   │   │   ├── TransformNode.tsx  ← JSONata, Script
    │   │   │   │   ├── UtilityNode.tsx    ← Pass, Wait, Branch
    │   │   │   │   └── SubFlowNode.tsx    ← Sub-flow invocation
    │   │   │   ├── Palette/
    │   │   │   │   └── NodePalette.tsx    ← Category browser + search
    │   │   │   └── Properties/
    │   │   │       └── PropertyPanel.tsx  ← Config / Info / Template tabs
    │   │   │
    │   │   ├── hooks/                     ← Custom React hooks
    │   │   │   ├── useAutoLayout.ts       ← ELKJS auto-arrange
    │   │   │   ├── useDragDrop.ts         ← Palette → canvas drag
    │   │   │   ├── useExecutionProgress.ts ← Execution animation
    │   │   │   ├── useFavorites.ts        ← Favorite persistence
    │   │   │   ├── useKeyboardShortcuts.ts ← Keyboard shortcuts
    │   │   │   └── useNodeValidity.ts     ← Real-time validation
    │   │   │
    │   │   ├── library/                   ← Reusable step library
    │   │   │   ├── LibraryService.ts      ← Template CRUD
    │   │   │   └── StepLibraryStore.ts    ← Zustand store
    │   │   │
    │   │   ├── schema-types/
    │   │   │   └── schema.ts              ← TypeScript interfaces
    │   │   │
    │   │   ├── schemas/                   ← 15 step schemas
    │   │   │   ├── index.ts               ← Registry + node type map
    │   │   │   ├── categories.ts          ← Category definitions
    │   │   │   └── steps/                 ← Per-category schemas
    │   │   │       ├── ai.ts              ← AI Decision, AI Text
    │   │   │       ├── rule.ts            ← Rule Engine, MS Rules
    │   │   │       ├── data.ts            ← SQL, DuckDB, EAV
    │   │   │       ├── api.ts             ← HTTP, Registered API
    │   │   │       ├── transform.ts       ← JSONata, Script
    │   │   │       ├── utility.ts         ← Pass, Wait, Branch
    │   │   │       └── subflow.ts         ← Sub-flow invoke
    │   │   │
    │   │   ├── services/                  ← Backend API services
    │   │   │   ├── flowService.ts         ← Flow save/load
    │   │   │   ├── executionService.ts    ← Start/stop execution
    │   │   │   ├── flowMigrationService.ts ← Import legacy flows
    │   │   │   └── apiRegistryService.ts  ← API registry
    │   │   │
    │   │   ├── stores/                    ← Zustand state stores
    │   │   │   ├── useNodeStore.ts        ← Canvas nodes
    │   │   │   ├── useEdgeStore.ts        ← Canvas edges
    │   │   │   ├── useExecutionStore.ts   ← Execution state
    │   │   │   ├── useUndoRedoStore.ts    ← Undo/redo history
    │   │   │   ├── useViewportStore.ts    ← Zoom, pan, selection
    │   │   │   ├── usePaletteStore.ts     ← Palette search/collapse
    │   │   │   └── useSettingsStore.ts    ← Theme, snap-to-grid
    │   │   │
    │   │   ├── utils/                     ← Utilities
    │   │   │   ├── validation.ts          ← Validation helpers
    │   │   │   ├── colors.ts              ← Category accent colors
    │   │   │   ├── id.ts                  ← UUID generation
    │   │   │   └── layout.ts              ← Position helpers
    │   │   │
    │   │   ├── test/                      ← Vitest test suite
    │   │   │   ├── schema.test.ts         ← Schema validation (111 tests)
    │   │   │   ├── stores.test.ts         ← Store operations (36 tests)
    │   │   │   ├── library.test.ts        ← Template CRUD (9 tests)
    │   │   │   ├── flowService.test.ts    ← Flow export/import (6 tests)
    │   │   │   ├── validation.test.ts     ← Validation logic (12 tests)
    │   │   │   └── utils.test.ts          ← Utility functions (12 tests)
    │   │   │
    │   │   └── styles/
    │   │       └── globals.css            ← Tailwind + custom styles
    │   │
    │   ├── package.json
    │   ├── vite.config.ts                 ← Builds into ../dist/
    │   ├── tsconfig.json
    │   ├── tailwind.config.js
    │   └── vitest.config.ts
    ├── Program.cs                 ← Host; serves frontend from dist/
    ├── StepFunctions/             ← Execution engine (interpreter, invoker, state store)
    ├── Controllers/               ← REST API: flows, tests, fake data endpoints
    ├── Flows/                     ← Flow definitions (JSON)
    ├── Converters/                ← BPMN converter
    ├── Mcp/                       ← MCP endpoint for AI harnesses
    ├── StepFunctionsApp.Tests/    ← xUnit backend test suite (engine, flow state, MCP, scenarios)
    ├── Stepflow-Builder-Tests/    ← Standalone fake test API host (http://localhost:5095)
    ├── start.ps1                  ← One-command dev launcher (backend + frontend)
    └── dist/                      ← Vite build output, served by .NET at /
```

---

## Testing

The frontend test suite covers **283 tests** across 13 files:

| Test File | Count | Coverage |
|:----------|:-----:|:---------|
| `schema.test.ts` | 153 | Schema structure, inputs/outputs, config fields, validation rules |
| `stores.test.ts` | 31 | Node, edge, execution, undo/redo store operations |
| `execution.test.ts` | 13 | Execution mode/store, ASL state translation, simulated browser engine |
| `validation.test.ts` | 12 | Validation logic, type checking |
| `utils.test.ts` | 12 | ID generation, color utilities, layout helpers |
| `connectionCompat.test.ts` | 11 | Port type compatibility and connectability checks |
| `variables.test.ts` | 11 | Variable interpolation, in-scope collection, subflow row fields |
| `flowHealth.test.ts` | 9 | Flow health hints (e.g. Map without iterator flow) |
| `library.test.ts` | 9 | Template CRUD, search, category filtering |
| `mapIterator.test.ts` | 8 | Map iterator body scaffolding and validation |
| `flowService.test.ts` | 6 | Flow save/load, export format (2 skipped) |
| `aiAssistant.test.ts` | 5 | AI assistant store behavior |
| `templates.test.ts` | 3 | Canonical EAV row-processing template |

```bash
# Run all tests
npm test

# Run one-shot (CI-friendly)
npx vitest run

# Run specific file
npx vitest run schema.test

# Watch mode with UI
npm run test:ui
```

### Backend Tests (.NET)

The .NET side has an xUnit suite (`StepFunctionsApp.Tests/`) covering the execution engine, durable flow-state checkpoint/recovery, MCP endpoints, and end-to-end scenario flows that run against the fake test APIs below:

```bash
dotnet test StepFunctionsApp.Tests --nologo
```

### Fake Test APIs (port 5095)

`Stepflow-Builder-Tests/` is a standalone .NET host exposing deterministic fake endpoints so flows can be tested without external services. Dev runs bind `http://localhost:5095`; the test suite hosts the same entry point in-memory via `WebApplicationFactory`.

```bash
dotnet run --project Stepflow-Builder-Tests    # or just run .\start.ps1, which starts it too
```

The sample flows in `Flows/FakeData_*.json` target these endpoints directly:

| Endpoint | Method | Purpose |
|:---------|:-------|:--------|
| `/api/fake/weather?city=&units=` | GET | Deterministic weather per city |
| `/api/fake/gold-price?currency=&amount=` | GET | Gold price; `amount` returns the total in `totalValue` |
| `/api/fake/exchange-rate?from=&to=&amount=` | GET | FX conversion (e.g. USD→NZD) with optional amount |
| `/api/fake/json/records?count=&seed=` | GET | Large nested JSON array (default 1000 records) for DuckDB transforms |
| `/api/fake/xml/invoices?count=&seed=` | GET | XML invoice document as text |
| `/api/fake/csv/sample?rows=&seed=` | GET | Raw CSV file content |
| `/api/fake/csv/import` | POST | Import CSV (multipart file upload, raw `text/csv` body, or JSON `{csv}`) into type-coerced rows |
| `/api/fake/payments/charge` | POST | Card charge; last4 `0002` declines deterministically |
| `/api/fake/receipts` | POST | Base64-encoded PDF receipt |
| `/api/fake/email/send`, `/api/fake/email/outbox` | POST/GET | In-memory email outbox for assertions |
| `/api/fake/carriers/pickup` | POST | Schedule carrier pickup; rejects unknown carriers |

Responses are deterministic (seeded where applicable), so flow tests can assert exact values.

---

## State Management

All state is managed via **Zustand** stores — no Redux boilerplate, no Context provider hell.

| Store | Responsibility |
|:------|:---------------|
| `useNodeStore` | Add/remove/update nodes, selection, position |
| `useEdgeStore` | Add/remove edges, connection validation |
| `useExecutionStore` | Execution lifecycle (idle → running → completed/failed) |
| `useUndoRedoStore` | 50-entry undo/redo history for node changes |
| `useViewportStore` | Zoom level, pan position, fit-to-screen |
| `usePaletteStore` | Search query, collapsed categories, favorites |
| `useSettingsStore` | Theme, snap-to-grid, auto-save preferences |

### Data Flow

```
Palette drag → useNodeStore.addNode() → Canvas renders node
Canvas click → selectedNodeId → PropertyPanel opens
PropertyPanel change → useNodeStore.updateNodeData() → Node re-renders
Canvas connect → useEdgeStore.addEdge() → Edge renders
Header Run → ExecutionService → useExecutionStore → Node animations
Header Save → FlowService → localStorage
```

---

## Backend Integration

The **.NET 10 backend** (`StepFunctions/`) is production-quality and kept as-is. It provides:

- **StepFunctionInterpreter** — Amazon States Language compatible execution
- **ResourceInvoker** — Scheme-based routing (`ai://`, `rule://`, `sql://`, `flow://`, `http://`)
- **Execution engines** — DuckDB, RulesEngine, JSONata, AI Decision
- **Flow storage** — JSON-based, loadable at startup
- **BPMN converter** — Converts BPMN XML to state machines

### API Endpoints

The frontend communicates with the backend via these endpoints:

| Endpoint | Method | Purpose |
|:---------|:------:|:--------|
| `/api/state-machines` | GET | List all flows |
| `/api/state-machines/:id` | GET | Get flow definition |
| `/api/state-machines/:id/execute` | POST | Execute flow |
| `/api/state-machines/:id/stop` | POST | Stop execution |
| `/api/execution/:id` | GET | Get execution status |
| `/api/api-registry` | GET | List registered APIs |

### Resource URI Scheme

| Scheme | Handler | Example |
|:-------|:--------|:--------|
| `ai://` | AI/LLM decision | `ai://decision`, `ai://decide/LMStudio` |
| `rule://` | RulesEngine | `rule://ComplianceCheck` |
| `sql://` | SQL query | `sql://customers` |
| `duckdb://` | DuckDB analytics | `duckdb://query` |
| `eav://` | EAV operations | `eav://lookup` |
| `http://` / `https://` | HTTP requests | `https://api.example.com/charge` |
| `api://` | Registered API | `api://payment_api_001` |
| `transform://` | JSONata/script | `transform://query` |
| `flow://` | Sub-flow call | `flow://CustomerValidation` |

---

## MCP Server

The backend exposes a **Model Context Protocol (MCP)** server at `/mcp`, letting AI agents (Claude Desktop, VS Code Copilot, or any MCP client) list, create, inspect and run flows through the same .NET engine as the UI. Transport: streamable HTTP — stateless, so no session handshake is required per request.

### Tools

| Tool | Purpose | Parameters |
|:-----|:--------|:-----------|
| `list_flows` | List registered flows (id, name, description, updatedAt) | — |
| `get_flow` | Full flow definition as camelCase ASL + metadata | `idOrName` |
| `save_flow` | Create or replace a flow by name; validates `startAt` and state references | `name`, `statesJson`, `description?`, `startAt?` (defaults to first key) |
| `run_flow` | Run synchronously; returns status, final output and per-state history | `idOrName`, `inputJson?` |

Definitions use the same camelCase Amazon States Language format the React UI exports (`startAt`, `states`, `type`, `next`, …), so a flow fetched via MCP can be re-imported into the canvas unchanged. State names keep their original casing. Supported state types: `Task`, `Pass`, `Choice`, `Wait`, `Parallel`, `Map`, `Succeed`, `Fail`. Errors are returned as JSON (`{"error": "…"}`) rather than thrown, so agents can read and react to them.

### Client Configuration

```json
{
  "mcpServers": {
    "stepflow": {
      "url": "http://localhost:5001/mcp"
    }
  }
}
```

Start the backend first (`dotnet run --project StepFunctionsApp`), then add the config to your MCP client. Example manual call:

```bash
curl -s http://localhost:5001/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

---

## Flow Migration

The **FlowMigrationService** imports existing flows from the `Flows/` directory (Amazon States Language format) and converts them to canvas nodes/edges.

### Supported Legacy Flows

| Flow | Description |
|:-----|:------------|
| `OrderProcess` | Order processing with credit checks and AI review |
| `REG_INT_BTP` | Bank file to authority payments pipeline |
| `ParentFlow` | Sub-flow composition demo |
| `WebBrowserDemo` | Playwright web automation with AI analysis |

### Migration Process

```typescript
import { FlowMigrationService } from '@services/flowMigrationService';

// List available legacy flows
const flows = await FlowMigrationService.listLegacyFlows();

// Import a specific flow
const legacyFlow = await FlowMigrationService.getLegacyFlow('OrderProcess');
await FlowMigrationService.importFlow(legacyFlow);

// Export current canvas as legacy format
const exported = await FlowMigrationService.exportToLegacy();
```

### Legacy → Schema Mapping

| Legacy Type/Resource | Schema ID |
|:---------------------|:----------|
| `ai://` | `stepflow:ai:decision` |
| `rule://` | `stepflow:rule:rule_engine` |
| `sql://` | `stepflow:data:sql` |
| `duckdb://` | `stepflow:data:duckdb` |
| `eav://` | `stepflow:data:eav` |
| `http://` / `https://` | `stepflow:api:http` |
| `api://` | `stepflow:api:registered` |
| `transform://` | `stepflow:transform:jsonata` |
| `flow://` | `stepflow:subflow:invoke` |
| `Pass` type | `stepflow:utility:pass` |
| `Choice` type | `stepflow:rule:rule_engine` |
| `Parallel` type | `stepflow:utility:branch` |

---

## Roadmap

### Phase 1-8: ✅ Complete

- [x] Project scaffolding (Vite + React 18 + xyflow + Tailwind)
- [x] Node schema system (15 schemas, 7 categories)
- [x] Custom node components per category
- [x] Canvas with drag-drop and connections
- [x] Property panel with dynamic config forms
- [x] Validation system with visual feedback
- [x] Auto-layout (ELKJS), undo/redo, keyboard shortcuts
- [x] Reusable step library with templates
- [x] Backend services (save/load, execution, migration)
- [x] Test suite (186 tests)

### Future Work

| Feature | Priority | Description |
|:--------|:--------:|:------------|
| **Dark/Light theme** | High | Theme toggle in settings |
| **Minimap zoom controls** | High | Pinch-to-zoom, scroll zoom |
| **Edge labels** | Medium | Show data type on connections |
| **Node collapse/expand** | Medium | Compact mode for large flows |
| **Context menus** | Medium | Right-click node actions |
| **Multi-flow tabs** | Medium | Open multiple flows simultaneously |
| **BPMN import** | Low | Convert BPMN XML to canvas |
| **Collaboration** | Low | Real-time multi-user editing |
| **Cloud storage** | Low | Save flows to backend API |

---

## License

This project is part of the StepFlow Builder codebase. See the root `LICENSE` file for details.

---

## References

- [xyflow (React Flow) Documentation](https://xyflow.com/docs)
- [chaiNNer — Node Editor Reference](https://github.com/chaiNNer-org/chaiNNer)
- [Zustand — State Management](https://github.com/pmndrs/zustand)
- [Amazon States Language](https://docs.aws.amazon.com/step-functions/home.html)
- [ELKJS — Graph Layout](https://www.eclipse.org/lemminx/elk/)
