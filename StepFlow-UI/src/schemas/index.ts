// ═══════════════════════════════════════════════════════════
// Schema Registry — Central lookup for all step schemas
// ═══════════════════════════════════════════════════════════

import { StepSchema, StepCategory, StepLibraryEntry, CategoryDefinition, NodeData } from '@schema-types/schema';
import { categoryById } from './categories';

// ── Import all step schemas ──
import { aiDecisionSchema, aiTextSchema } from './steps/ai';
import { ruleEngineSchema, msRulesEngineSchema } from './steps/rule';
import { sqlQuerySchema, duckDbQuerySchema, eavOperationSchema } from './steps/data';
import { httpRequestSchema, registeredApiSchema } from './steps/api';
import { jsonataSchema, scriptSchema } from './steps/transform';
import { passThroughSchema, waitSchema, branchSchema } from './steps/utility';
import { subFlowSchema } from './steps/subflow';
import { humanTaskSchema } from './steps/human';
import { formCaptureSchema } from './steps/formCapture';
import { startStateSchema, endStateSchema } from './steps/terminal';
import { choiceStateSchema, mapStateSchema, parallelStateSchema, succeedStateSchema, failStateSchema } from './steps/flow';
import { sshCommandSchema } from './steps/ssh';
import { fetchFilesSchema } from './steps/fetch';

// ── Master Schema Array ──
export const stepSchemas: StepSchema[] = [
  // Terminal (2)
  startStateSchema,
  endStateSchema,
  // Flow (5)
  choiceStateSchema,
  mapStateSchema,
  parallelStateSchema,
  succeedStateSchema,
  failStateSchema,
  // AI (2)
  aiDecisionSchema,
  aiTextSchema,
  // Rule (2)
  ruleEngineSchema,
  msRulesEngineSchema,
  // Data (3)
  sqlQuerySchema,
  duckDbQuerySchema,
  eavOperationSchema,
  // API (2)
  httpRequestSchema,
  registeredApiSchema,
  // Transform (2)
  jsonataSchema,
  scriptSchema,
  // Utility (3)
  passThroughSchema,
  waitSchema,
  branchSchema,
  // SubFlow (1)
  subFlowSchema,
  // Human (1)
  humanTaskSchema,
  // Form Capture (1)
  formCaptureSchema,
  // Remote (1)
  sshCommandSchema,
  // File Transfer (1)
  fetchFilesSchema,
];

// ═══════════════════════════════════════════════════════════
// Lookup Maps
// ═══════════════════════════════════════════════════════════

// schemaId → StepSchema
export const schemaById = new Map<string, StepSchema>(
  stepSchemas.map((s) => [s.schemaId, s])
);

// StepCategory → StepSchema[]
export const schemasByCategory = new Map<StepCategory, StepSchema[]>();
for (const schema of stepSchemas) {
  const existing = schemasByCategory.get(schema.category) ?? [];
  existing.push(schema);
  schemasByCategory.set(schema.category, existing);
}

// ═══════════════════════════════════════════════════════════
// Category Node Component Map (placeholder — real components in Phase 3)
// ═══════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════
// Category Node Component Map (Phase 2 — Custom Nodes)
// ═══════════════════════════════════════════════════════════

import { AiNode } from '@components/Nodes/AiNode';
import { RuleNode } from '@components/Nodes/RuleNode';
import { DataNode } from '@components/Nodes/DataNode';
import { ApiNode } from '@components/Nodes/ApiNode';
import { TransformNode } from '@components/Nodes/TransformNode';
import { UtilityNode } from '@components/Nodes/UtilityNode';
import { SubFlowNode } from '@components/Nodes/SubFlowNode';
import { TerminalNode } from '@components/Nodes/TerminalNode';
import { FlowNode } from '@components/Nodes/FlowNode';
import { HumanTaskNode } from '@components/Nodes/HumanTaskNode';
import { FormCaptureNode } from '@components/Nodes/FormCaptureNode';
import { SshNode } from '@components/Nodes/SshNode';
import { FetchFilesNode } from '@components/Nodes/FetchFilesNode';

export const categoryNodeComponents: Record<StepCategory, React.ComponentType<{ id: string; data: NodeData; selected?: boolean }>> = {
  terminal: TerminalNode,
  flow: FlowNode,
  ai: AiNode,
  rule: RuleNode,
  data: DataNode,
  api: ApiNode,
  transform: TransformNode,
  utility: UtilityNode,
  subflow: SubFlowNode,
  human: HumanTaskNode,
  formcapture: FormCaptureNode,
  remote: SshNode,
  transfer: FetchFilesNode,
};

// Build xyflow nodeTypes map: { "stepflow:ai:decision": AiNode, ... }
// NOTE: This is a function (not a top-level const) to avoid circular dependency issues.
// The schemas module imports node components which import stores which import back here.
// Computing this lazely ensures all imports are fully resolved when nodeTypes is accessed.
export function getStepNodeTypes(): Record<string, React.ComponentType<{ id: string; data: NodeData; selected?: boolean }>> {
  return Object.fromEntries(
    stepSchemas.map((schema) => [
      schema.schemaId,
      categoryNodeComponents[schema.nodeComponent],
    ])
  ) as Record<string, React.ComponentType<{ id: string; data: NodeData; selected?: boolean }>>;
}

// ═══════════════════════════════════════════════════════════
// Palette Data (for NodePalette)
// ═══════════════════════════════════════════════════════════

export interface PaletteCategory {
  id: StepCategory;
  name: string;
  icon: string;
  color: string;
  items: PaletteItem[];
}

export interface PaletteItem {
  id: string;
  name: string;
  schemaId: string;
  color: string;
  description?: string;
  tags: string[];
}

export function buildPaletteData(): PaletteCategory[] {
  const palette: PaletteCategory[] = [];

  for (const category of categoryById.values()) {
    const schemas = schemasByCategory.get(category.id) ?? [];
    palette.push({
      id: category.id,
      name: category.name,
      icon: category.icon,
      color: category.color,
      items: schemas.map((s) => ({
        id: s.schemaId,
        name: s.name,
        schemaId: s.schemaId,
        color: s.color,
        description: s.description,
        tags: s.tags,
      })),
    });
  }

  return palette;
}

export const paletteData = buildPaletteData();

// ═══════════════════════════════════════════════════════════
// Schema Service — Runtime Resolution
// ═══════════════════════════════════════════════════════════

export interface SchemaService {
  getSchemaById(schemaId: string): StepSchema | undefined;
  getSchemaByCategory(category: StepCategory): StepSchema[];
  getAllSchemas(): StepSchema[];
  getActiveSchemas(): StepSchema[]; // Non-deprecated
  searchSchemas(query: string): StepSchema[];
  getCategories(): CategoryDefinition[];
  getCategoryById(id: StepCategory): CategoryDefinition | undefined;
  getNodeComponentForSchema(schemaId: string): React.ComponentType<{ id: string; data: NodeData; selected?: boolean }> | undefined;
  getNodeComponentForCategory(category: StepCategory): React.ComponentType<{ id: string; data: NodeData; selected?: boolean }>;

}

export const schemaService: SchemaService = {
  getSchemaById(schemaId: string): StepSchema | undefined {
    return schemaById.get(schemaId);
  },

  getSchemaByCategory(category: StepCategory): StepSchema[] {
    return schemasByCategory.get(category) ?? [];
  },

  getAllSchemas(): StepSchema[] {
    return stepSchemas;
  },

  getActiveSchemas(): StepSchema[] {
    return stepSchemas.filter((s) => !s.deprecated);
  },

  searchSchemas(query: string): StepSchema[] {
    const q = query.toLowerCase();
    return stepSchemas.filter(
      (s) =>
        s.name.toLowerCase().includes(q) ||
        s.description.toLowerCase().includes(q) ||
        s.tags.some((t) => t.toLowerCase().includes(q)) ||
        s.schemaId.toLowerCase().includes(q)
    );
  },

  getCategories(): CategoryDefinition[] {
    return Array.from(categoryById.values());
  },

  getCategoryById(id: StepCategory): CategoryDefinition | undefined {
    return categoryById.get(id);
  },

  getNodeComponentForSchema(schemaId: string): React.ComponentType<{ id: string; data: NodeData; selected?: boolean }> | undefined {
    const schema = schemaById.get(schemaId);
    if (!schema) return undefined;
    return categoryNodeComponents[schema.nodeComponent];
  },

  getNodeComponentForCategory(category: StepCategory): React.ComponentType<{ id: string; data: NodeData; selected?: boolean }> {
    return categoryNodeComponents[category];
  },
};

// ═══════════════════════════════════════════════════════════
// Reusable Step Library (pre-built templates)
// ═══════════════════════════════════════════════════════════

export const stepLibrary: StepLibraryEntry[] = [
  // AI Templates
  {
    id: 'lib:ai:credit-decision-v1',
    schemaId: 'stepflow:ai:decision',
    name: 'Credit Decision — Standard',
    description: 'Evaluate creditworthiness using GPT-4 with risk scoring',
    category: 'ai',
    version: '1.0.0',
    configuration: {
      model: 'gpt-4-turbo',
      prompt: 'Evaluate credit risk based on applicant data. Return a risk score from 0-100.',
      temperature: 0.3,
      llmService: 'azureOpenAI',
      outputFormat: 'json',
      maxTokens: 500,
    },
    inputs: [
      { id: 'input_data', label: 'Applicant Data', type: 'json', optional: false, position: 'left' },
    ],
    outputs: [
      { id: 'output_result', label: 'Risk Assessment', type: 'json', position: 'right' },
      { id: 'output_decision', label: 'Approved', type: 'boolean', position: 'right' },
    ],
    tags: ['credit', 'finance', 'ai', 'risk'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
  {
    id: 'lib:ai:sentiment-analysis-v1',
    schemaId: 'stepflow:ai:text',
    name: 'Sentiment Analysis',
    description: 'Analyze sentiment of text input using GPT-4',
    category: 'ai',
    version: '1.0.0',
    configuration: {
      model: 'gpt-4-turbo',
      systemPrompt: 'Analyze the sentiment of the provided text. Return positive, negative, or neutral with confidence score.',
      temperature: 0.1,
      llmService: 'azureOpenAI',
      maxTokens: 300,
      topP: 0.9,
    },
    inputs: [
      { id: 'input_topic', label: 'Text', type: 'string', optional: false, position: 'left' },
    ],
    outputs: [
      { id: 'output_text', label: 'Sentiment', type: 'string', position: 'right' },
    ],
    tags: ['sentiment', 'nlp', 'ai', 'analysis'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
  // Data Templates
  {
    id: 'lib:data:customer-lookup-v1',
    schemaId: 'stepflow:data:sql',
    name: 'Customer Lookup',
    description: 'Look up customer record by ID with caching',
    category: 'data',
    version: '1.0.0',
    configuration: {
      query: 'SELECT * FROM customers WHERE id = @customerId',
      commandTimeout: 5000,
      enableCaching: true,
      cacheTTL: 600,
    },
    inputs: [
      { id: 'input_parameters', label: 'Params', type: 'json', optional: true, position: 'left' },
    ],
    outputs: [
      { id: 'output_rows', label: 'Customer', type: 'array', position: 'right' },
    ],
    tags: ['customer', 'lookup', 'sql', 'data'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
  {
    id: 'lib:data:analytics-query-v1',
    schemaId: 'stepflow:data:duckdb',
    name: 'Analytics Dashboard Query',
    description: 'Run analytics queries on in-memory data',
    category: 'data',
    version: '1.0.0',
    configuration: {
      query: 'SELECT category, COUNT(*) as count, AVG(value) as avg_value FROM data GROUP BY category',
      tableName: 'analytics_data',
      maxRows: 1000,
    },
    inputs: [
      { id: 'input_data', label: 'Data', type: 'json', optional: false, position: 'left' },
    ],
    outputs: [
      { id: 'output_result', label: 'Results', type: 'array', position: 'right' },
    ],
    tags: ['analytics', 'duckdb', 'aggregation', 'data'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
  // API Templates
  {
    id: 'lib:api:payment-gateway-v1',
    schemaId: 'stepflow:api:http',
    name: 'Payment Gateway',
    description: 'Process payment via HTTP request to payment provider',
    category: 'api',
    version: '1.0.0',
    configuration: {
      method: 'POST',
      headers: JSON.stringify({ 'Content-Type': 'application/json', 'Authorization': 'Bearer ${PAYMENT_API_KEY}' }),
      timeout: 30000,
      retryCount: 2,
      retryDelay: 3000,
      authentication: 'bearer',
    },
    inputs: [
      { id: 'input_body', label: 'Payment Data', type: 'json', optional: true, position: 'left' },
    ],
    outputs: [
      { id: 'output_response', label: 'Response', type: 'json', position: 'right' },
    ],
    tags: ['payment', 'gateway', 'http', 'api'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
  {
    id: 'lib:api:email-service-v1',
    schemaId: 'stepflow:api:http',
    name: 'Email Service',
    description: 'Send email via HTTP request to email provider',
    category: 'api',
    version: '1.0.0',
    configuration: {
      method: 'POST',
      headers: JSON.stringify({ 'Content-Type': 'application/json' }),
      timeout: 15000,
      retryCount: 1,
      retryDelay: 2000,
      authentication: 'none',
    },
    inputs: [
      { id: 'input_body', label: 'Email Data', type: 'json', optional: true, position: 'left' },
    ],
    outputs: [
      { id: 'output_response', label: 'Response', type: 'json', position: 'right' },
    ],
    tags: ['email', 'notification', 'http', 'api'],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    usageCount: 0,
    isPublished: true,
  },
];

// Library lookup maps
export const libraryById = new Map<string, StepLibraryEntry>(
  stepLibrary.map((e) => [e.id, e])
);

export const libraryByCategory = new Map<StepCategory, StepLibraryEntry[]>();
for (const entry of stepLibrary) {
  const existing = libraryByCategory.get(entry.category) ?? [];
  existing.push(entry);
  libraryByCategory.set(entry.category, existing);
}

// ═══════════════════════════════════════════════════════════
// Re-exports
// ═══════════════════════════════════════════════════════════

export { categoryDefinitions, categoryById, categoryColorById, categoryNodeComponentMap } from './categories';
export type {
  StepSchema,
  StepInput,
  StepOutput,
  ConfigField,
  ConfigFieldType,
  ValidationRule,
  Validity,
  StepCategory,
  CategoryDefinition,
  StepLibraryEntry,
  NodeData,
  DataType,
} from '@schema-types/schema';
