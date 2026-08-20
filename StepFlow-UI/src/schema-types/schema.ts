// ═══════════════════════════════════════════════════════════
// Core Types — chaiNNer-inspired Node Schema System
// ═══════════════════════════════════════════════════════════

// ── Data Types (for edge type-checking) ──
export type DataType =
  | 'json'        // Arbitrary JSON object
  | 'string'      // Text string
  | 'number'      // Numeric value
  | 'boolean'     // True/false
  | 'array'       // JSON array
  | 'image'       // Image data
  | 'any';        // Accepts any type

// ── Step Input (node handle — input side) ──
export interface StepInput {
  id: string;              // "input_data", "input_config"
  label: string;           // "Input Data", "Configuration"
  type: DataType;          // For type-checking connections
  optional: boolean;       // Can be unconnected
  description?: string;    // Tooltip text
  position: 'left' | 'top'; // Handle position
}

// ── Step Output (node handle — output side) ──
export interface StepOutput {
  id: string;
  label: string;
  type: DataType;
  description?: string;
  position: 'right' | 'bottom';
  /** Optional typed fields of a structured (json) output — enables `{{output.field}}` variable references. */
  fields?: Array<{ name: string; type: DataType }>;
}

// ── Config Field (property panel form field) ──
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
  default?: unknown;
  options?: { label: string; value: unknown }[];  // For dropdown
  min?: number;
  max?: number;                  // For number/slider
  condition?: (nodeData: NodeData) => boolean; // Conditional visibility
  required?: boolean;
  description?: string;
  validate?: (value: unknown) => string | null;    // Custom validation
}

// ── Validation Rule ──
export interface ValidationRule {
  id: string;
  check: (nodeData: NodeData, connectedInputs: Set<string>) => Validity;
}

export interface Validity {
  id?: string;
  isValid: boolean;
  reason?: string;
}

// ── Step Category ──
export type StepCategory =
  | 'terminal'   // START / END states (visual anchors)
  | 'flow'       // Flow control states: Choice, Wait, Map, Parallel, Pass, Succeed, Fail
  | 'ai'
  | 'rule'
  | 'data'
  | 'api'
  | 'transform'
  | 'utility'
  | 'subflow'
  | 'human';   // Human-in-the-loop approval / manual action states

// ── Category Definition ──
export interface CategoryDefinition {
  id: StepCategory;
  name: string;              // "AI & LLM"
  description: string;
  icon: string;
  color: string;
  nodeComponent: string;     // React component name, e.g., "AiNode", "DataNode"
}

// ── Step Schema (the master definition) ──
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
  nodeComponent: StepCategory; // e.g., "ai" → AiNode, "data" → DataNode
}

// ── Reusable Step Library Entry ──
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

// ── Node Data (runtime node state) ──
export interface NodeData {
  schemaId: string;
  label?: string;
  color?: string;
  description?: string;
  configuration?: Record<string, unknown>;
  isCollapsed?: boolean;
  isDisabled?: boolean;
  templateId?: string;       // If this node originated from a template
  [key: string]: unknown;
}
