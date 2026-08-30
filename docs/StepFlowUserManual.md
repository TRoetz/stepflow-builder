# StepFlow Builder — User Manual

## Table of Contents

1. [Overview](#1-overview)
2. [Interface Tour](#2-interface-tour)
3. [Your First Workflow](#3-your-first-workflow)
4. [States Reference](#4-states-reference)
5. [Connecting States](#5-connecting-states)
6. [Configuring States](#6-configuring-states)
7. [Canvas Navigation](#7-canvas-navigation)
8. [Keyboard Shortcuts](#8-keyboard-shortcuts)
9. [Saving and Exporting](#9-saving-and-exporting)
10. [Tips and Best Practices](#10-tips-and-best-practices)

---

## 1 Overview

StepFlow Builder is a visual workflow designer for building, testing, and deploying automation pipelines. Each workflow is a directed graph of **states** — individual steps that perform work or control execution flow.

### Core Concepts

- **Workflow** — A complete pipeline from start to finish, composed of connected states.
- **State** — A single step in a workflow. States fall into two categories:
  - **Flow States** — Control the execution path (branching, looping, parallelism, termination).
  - **Task States** — Perform actual work by delegating to an external handler (AI, database, HTTP, script).
- **START** — The entry point of every workflow. A visual marker with no inputs.
- **END** — The exit point of a workflow. A visual marker with no outputs.
- **Connection** — A directed edge linking one state's output to another state's input.

### Workflow Anatomy

```
┌──────────┐     ┌──────────┐     ┌──────────┐
│  START   │────▶│  Choice  │────▶│  END     │
└──────────┘     │  ┌────┐  │     └──────────┘
                 ├─┤True│  │
                 │  └────┘  │
                 ├──────────┤
                 │  ┌────┐  │
                 └─┤False│─▶┼──▶ ... more states
                   └────┘   │
                            │
```

---

## 2 Interface Tour

The StepFlow Builder interface has three main panels:

### Top Bar

| Control | Description |
|---|---|
| **Flow Name** | Editable text field to name your workflow |
| **Run** (green) | Execute the current workflow |
| **Stop** (red) | Stop a running execution |
| **Save** | Persist the workflow |
| **Undo / Redo** | Step back or forward through edits |
| **Auto-Layout** | Automatically arrange nodes on the canvas |
| **Toggle Palette** | Show/hide the left sidebar |
| **Toggle Properties** | Show/hide the right sidebar |
| **Import / Export** | Load or save workflow as JSON |
| **Settings** | Application settings |

### Left Panel — States Palette

The palette lists all available states organized by category. Each category is collapsible.

| Category | Icon | States |
|---|---|---|
| Terminal | ⚡ | START, END |
| Flow Control | 🔀 | Choice, Map, Parallel, Succeed, Fail |
| AI & LLM | 🤖 | AI Decision, AI Text Generation |
| Rule Engine | ⚖️ | Rule Check, Rule Evaluate |
| Data | 📊 | SQL Query, Data Fetch, Data Store |
| API | 🔌 | HTTP Request, API Call |
| Transform | ⚙️ | Transform, Script |
| Utility | 🔧 | Pass Through, Wait, Branch |
| Sub-Flow | 📦 | Sub-Flow |

**Palette Features:**

- **Search** — Filter states by name or tag using the search bar
- **Favorites** — Star frequently-used states for quick access (appears at the top of the palette)
- **Drag** — Drag a state from the palette onto the canvas to add it
- **Double-click** — Double-click a state in the palette to add it to the canvas center

### Right Panel — Properties

The properties panel shows configuration options for the currently selected state. Click any node on the canvas to view and edit its properties.

### Bottom Bar — Status

| Indicator | Description |
|---|---|
| **Execution** | Current execution status (Idle, Running, Completed, Failed) |
| **Validation** | Workflow validity (Valid, Warnings, Errors) |
| **Nodes** | Number of states on the canvas |
| **Edges** | Number of connections between states |
| **Zoom** | Current zoom level |

---

## 3 Your First Workflow

Follow these steps to build a simple workflow:

### Step 1 — Add a START State

1. Locate **START** in the palette under the ⚡ Terminal category
2. Drag it onto the canvas, or double-click it to add it

### Step 2 — Add a Task State

1. Find **AI Decision** under 🤖 AI & LLM
2. Drag it onto the canvas to the right of START

### Step 3 — Connect the States

1. Hover over the right side of the START node to reveal the **output handle** (a small circle)
2. Click and drag from the output handle to the **input handle** on the left side of the AI Decision node
3. A connection line appears when the link is valid

### Step 4 — Add an END State

1. Drag **END** from the palette onto the canvas
2. Connect the AI Decision output to the END input

### Step 5 — Configure the AI Decision

1. Click the AI Decision node to select it
2. In the Properties panel on the right, fill in:
   - **Resource URI** — The AI engine endpoint (e.g., `ai://classify`)
   - **Input Data** — The data expression to send to the AI handler
3. The node will show a green indicator when valid

### Step 6 — Run the Workflow

1. Click the **Run** button (or press `Ctrl+Enter`)
2. Watch the execution flow through each state
3. Check the status bar for the result

---

## 4 States Reference

### 4.1 Terminal States

#### START

Marks the entry point of the workflow. Every flow must have exactly one START state.

- **Inputs:** None
- **Outputs:** 1 (Start — passes input data into the flow)
- **Config:** Description (optional text to document the flow purpose)

#### END

Marks a completion point of the workflow. A flow can have multiple END states (one per branch).

- **Inputs:** 1 (End — receives final data from the preceding state)
- **Outputs:** None
- **Config:** Description (optional text to document the expected outcome)

### 4.2 Flow Control States

#### Choice

Conditional branching based on data comparisons. Routes execution down different paths depending on conditions.

- **Inputs:** 1 (Input — data to evaluate)
- **Outputs:** 2 (True, False — directed based on condition result)
- **Config:**
  - Condition expression (the logic to evaluate)
  - Default output (when condition cannot be evaluated)

#### Map

Iterates over a dataset and runs a sub-workflow for each item. Processes collections sequentially.

- **Inputs:** 1 (Items — an array of data)
- **Outputs:** 1 (Results — array of results from each iteration)
- **Config:**
  - Iterator variable name
  - Max concurrent iterations

#### Parallel

Spawns independent branches that run concurrently. All branches must complete before the flow continues.

- **Inputs:** 1 (Input — data shared across branches)
- **Outputs:** 1 (Results — combined output from all branches)
- **Config:**
  - Number of parallel branches
  - Timeout per branch

#### Succeed

Marks a successful terminal outcome. Stops execution with a success status.

- **Inputs:** 1 (Input — final data)
- **Outputs:** None
- **Config:**
  - Success message
  - Output data expression

#### Fail

Marks a failure terminal outcome. Stops execution with an error status.

- **Inputs:** 1 (Input — error context)
- **Outputs:** None
- **Config:**
  - Error message
  - Error code

### 4.3 AI & LLM States

#### AI Decision

Sends data to an AI engine for classification, routing, or decision-making.

- **Inputs:** 1 (Input — data for the AI to evaluate)
- **Outputs:** 1 (Output — AI decision result)
- **Config:**
  - Resource URI (e.g., `ai://classify`)
  - Input data expression
  - Model parameters

#### AI Text Generation

Generates text output using a language model.

- **Inputs:** 1 (Input — prompt or context)
- **Outputs:** 1 (Output — generated text)
- **Config:**
  - Resource URI (e.g., `ai://generate`)
  - Prompt template
  - Temperature, max tokens

### 4.4 Rule Engine States

#### Rule Check

Evaluates a single business rule against input data.

- **Inputs:** 1 (Input — data to check)
- **Outputs:** 2 (Passed, Failed)
- **Config:**
  - Rule expression
  - Rule description

#### Rule Evaluate

Evaluates multiple rules and returns structured results.

- **Inputs:** 1 (Input — data to evaluate)
- **Outputs:** 1 (Output — rule evaluation results)
- **Config:**
  - Rule set reference
  - Evaluation mode

### 4.5 Data States

#### SQL Query

Executes a SQL query against a configured database.

- **Inputs:** 1 (Input — query parameters)
- **Outputs:** 1 (Output — query results)
- **Config:**
  - Resource URI (e.g., `data://query`)
  - SQL statement
  - Parameter bindings

#### Data Fetch

Retrieves data from a data source by identifier.

- **Inputs:** 1 (Input — resource identifier)
- **Outputs:** 1 (Output — fetched data)
- **Config:**
  - Resource URI
  - Fetch options

#### Data Store

Persists data to a storage backend.

- **Inputs:** 1 (Input — data to store)
- **Outputs:** 1 (Output — storage confirmation)
- **Config:**
  - Resource URI
  - Storage key
  - TTL (time-to-live)

### 4.6 API States

#### HTTP Request

Makes an HTTP request to an external service.

- **Inputs:** 1 (Input — request payload)
- **Outputs:** 1 (Output — response data)
- **Config:**
  - Method (GET, POST, PUT, DELETE)
  - URL
  - Headers
  - Body template
  - Timeout

#### API Call

Calls a pre-registered API integration.

- **Inputs:** 1 (Input — call parameters)
- **Outputs:** 1 (Output — API response)
- **Config:**
  - API registry reference
  - Endpoint
  - Authentication

### 4.7 Transform States

#### Transform

Applies a data transformation using a configured engine.

- **Inputs:** 1 (Input — raw data)
- **Outputs:** 1 (Output — transformed data)
- **Config:**
  - Resource URI (e.g., `transform://jsonata`)
  - Transformation expression
  - Engine type

#### Script

Executes custom script logic for complex transformations.

- **Inputs:** 1 (Input — script input)
- **Outputs:** 1 (Output — script result)
- **Config:**
  - Script engine (JavaScript, Python)
  - Script body
  - Timeout

### 4.8 Utility States

#### Pass Through

Passes data through without modification. Useful for routing, labeling, or debugging.

- **Inputs:** 1 (Input — data)
- **Outputs:** 1 (Output — same data)
- **Config:**
  - Description
  - Output path override

#### Wait

Paes execution for a specified duration or until a timestamp.

- **Inputs:** 1 (Input — data to carry through)
- **Outputs:** 1 (Output — data after wait)
- **Config:**
  - Wait type (duration or timestamp)
  - Duration (seconds) or target timestamp

#### Branch

Conditional routing with multiple output paths based on data values.

- **Inputs:** 1 (Input — data to route)
- **Outputs:** Multiple (based on branch conditions)
- **Config:**
  - Branch conditions
  - Default branch

### 4.9 Sub-Flow

#### Sub-Flow

Invokes another StepFlow workflow as a nested step.

- **Inputs:** 1 (Input — data passed to the sub-flow)
- **Outputs:** 1 (Output — result from the sub-flow)
- **Config:**
  - Target flow reference
  - Input mapping
  - Timeout

---

## 5 Connecting States

### Creating Connections

1. **Hover** over a node to reveal connection handles (small circles)
2. **Drag** from an output handle (right side) to an input handle (left side) of another node
3. A **valid connection** shows a solid line; an invalid one shows a dashed red line

### Connection Rules

- Every state (except START) must have at least one input connection
- Every state (except END, Succeed, Fail) must have at least one output connection
- Connections are **directed** — data flows from output to input
- A node can have **multiple inputs** (fan-in) and **multiple outputs** (fan-out)

### Removing Connections

- **Right-click** a connection line and select **Delete**, or
- Select the connection and press **Delete** key

---

## 6 Configuring States

### Selecting a State

Click any node on the canvas to select it. The selected node shows a highlighted border and its properties appear in the right panel.

### Properties Panel

Each state exposes configuration fields relevant to its type:

| Field Type | Description |
|---|---|
| **Text** | Single-line text input |
| **Textarea** | Multi-line text input |
| **Number** | Numeric input with min/max constraints |
| **Dropdown** | Selection from predefined options |
| **Toggle** | Boolean on/off switch |
| **Code Editor** | Syntax-highlighted code area |
| **JSON Editor** | Structured JSON input with validation |
| **Color Picker** | Color selection |
| **Slider** | Range selection |

### Validation

The builder validates configuration in real time:

- **Green indicator** — All required fields are satisfied
- **Yellow indicator** — Warnings (optional fields missing)
- **Red indicator** — Errors (required fields missing or invalid values)

The status bar at the bottom shows overall workflow validity.

### Resource URIs

Task states use **Resource URIs** to route work to the appropriate engine:

| URI Scheme | Engine |
|---|---|
| `ai://` | AI and LLM operations |
| `rule://` | Business rule evaluation |
| `transform://` | Data transformation |
| `flow://` | Nested workflow invocation |
| `http://` | External API calls |
| `data://` | Database and data access |

---

## 7 Canvas Navigation

### Zoom and Pan

| Action | How |
|---|---|
| Zoom in | `+` button or `Ctrl +`滚 wheel |
| Zoom out | `-` button or `Ctrl +` scroll wheel |
| Fit view | Click the **Fit View** button |
| Pan | Click and drag on empty canvas area |

### Node Manipulation

| Action | How |
|---|---|
| Move a node | Click and drag the node body |
| Select a node | Click the node |
| Delete a node | Select and press `Delete`, or right-click and select **Delete** |
| Duplicate a node | Right-click and select **Duplicate** |

### Mini Map

The mini map in the corner shows an overview of the entire workflow. Click and drag the viewport rectangle to navigate.

---

## 8 Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl + Enter` | Run / Stop workflow |
| `Ctrl + S` | Save workflow |
| `Ctrl + Z` | Undo |
| `Ctrl + Y` | Redo |
| `Ctrl + Shift + F` | Auto-Layout |
| `Delete` | Delete selected node or connection |
| `Escape` | Deselect current selection |

---

## 9 Saving and Exporting

### Save

Click **Save** (or press `Ctrl + S`) to persist the current workflow. The flow name is used as the identifier.

### Export

Click the **Export** button to download the workflow definition as a JSON file. This file can be:

- Shared with other users
- Stored in version control
- Imported into another StepFlow Builder instance

### Import

Click the **Import** button and select a previously exported JSON file to load a workflow.

---

## 10 Tips and Best Practices

### Workflow Design

1. **Always start with START** — Every workflow should begin with a START state to define the entry point
2. **Plan your branches** — Use Choice states early to handle different input types or conditions
3. **Use descriptive names** — Name your flows and add descriptions to START/END states for documentation
4. **Keep flows focused** — Complex logic belongs in Sub-Flow nodes, not in a single monolithic flow
5. **Use Wait states** — When calling external services, add Wait states to handle rate limiting or async operations

### Canvas Organization

1. **Use Auto-Layout** — Let the builder arrange nodes automatically, then fine-tune by hand
2. **Group related states** — Keep connected states visually close for readability
3. **Leave space for branches** — When using Choice or Parallel states, leave room below for the branch paths

### Configuration

1. **Validate before running** — Check the validation indicator in the status bar before executing
2. **Test incrementally** — Run the flow after adding each new state to catch issues early
3. **Use favorites** — Star your most-used states for quick access

### Error Handling

1. **Add Fail states** — Provide explicit failure paths for error conditions
2. **Use Succeed states** — Mark successful branch endpoints for clarity
3. **Check connection validity** — Red dashed lines indicate invalid connections that will cause runtime errors

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────┐
│  STEP FLOW BUILDER — QUICK REFERENCE                │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ADD STATE:  Drag from palette → Canvas             │
│             Double-click palette item                │
│                                                     │
│  CONNECT:    Drag output handle → input handle       │
│                                                     │
│  CONFIGURE:  Click node → Edit in Properties panel   │
│                                                     │
│  RUN:        Click Run or Ctrl+Enter                 │
│  SAVE:       Click Save or Ctrl+S                    │
│                                                     │
│  NAVIGATE:   Scroll to zoom · Drag to pan            │
│             Fit View button to reset                  │
│                                                     │
│  DELETE:     Select + Delete key                     │
│                                                     │
│  PALETTE:    Search to filter · Star for favorites   │
│             Collapse categories to reduce clutter     │
│                                                     │
└─────────────────────────────────────────────────────┘
```
