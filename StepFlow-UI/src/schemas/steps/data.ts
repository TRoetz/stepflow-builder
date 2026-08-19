import { StepSchema, NodeData } from '@schema-types/schema';

// ── SQL Query ──
export const sqlQuerySchema: StepSchema = {
  schemaId: 'stepflow:data:sql',
  name: 'SQL Query',
  category: 'data',
  description: 'Execute SQL queries against a database connection.',
  icon: 'database',
  color: '#3B82F6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['sql', 'database', 'query', 'data'],
  nodeComponent: 'data',

  inputs: [
    { id: 'input_parameters', label: 'Parameters', type: 'json', optional: true, position: 'left' },
    { id: 'input_dataset', label: 'Dataset', type: 'array', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_rows', label: 'Rows', type: 'array', description: 'Query result rows', position: 'right' },
    { id: 'output_count', label: 'Row Count', type: 'number', description: 'Number of rows returned', position: 'right' },
  ],

  configFields: [
    {
      id: 'connectionString',
      label: 'Connection String',
      type: 'text',
      required: true,
      description: 'Database connection string',
    },
    {
      id: 'query',
      label: 'SQL Query',
      type: 'code',
      default: 'SELECT * FROM table WHERE id = @id',
      required: true,
      description:
        'SQL query to execute. Use @param for parameters or {{node.field}} variables to reference upstream data.',
    },
    {
      id: 'outputColumns',
      label: 'Output Columns',
      type: 'text',
      default: '',
      description:
        'Comma-separated result columns (e.g. "id, name"). Lets downstream nodes reference them as {{this_node.column}}.',
    },
    {
      id: 'commandTimeout',
      label: 'Command Timeout (ms)',
      type: 'number',
      default: 30000,
      min: 1000,
      max: 300000,
      description: 'Maximum time for query execution',
    },
    {
      id: 'enableCaching',
      label: 'Enable Caching',
      type: 'toggle',
      default: false,
      description: 'Cache query results',
    },
    {
      id: 'cacheTTL',
      label: 'Cache TTL (seconds)',
      type: 'number',
      default: 300,
      min: 1,
      max: 3600,
      condition: (data: NodeData) => data.configuration?.enableCaching === true,
      description: 'Time-to-live for cached results',
    },
  ],

  validation: [
    {
      id: 'connectionString_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.connectionString,
        reason: 'Connection string is required',
      }),
    },
    {
      id: 'query_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.query,
        reason: 'SQL query is required',
      }),
    },
  ],
};

// ── DuckDB Query ──
export const duckDbQuerySchema: StepSchema = {
  schemaId: 'stepflow:data:duckdb',
  name: 'DuckDB Query',
  category: 'data',
  description: 'Execute SQL queries using DuckDB for in-memory analytics.',
  icon: 'table',
  color: '#3B82F6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['duckdb', 'analytics', 'sql', 'in-memory'],
  nodeComponent: 'data',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_config', label: 'Config', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'array', description: 'Query result rows', position: 'right' },
    { id: 'output_schema', label: 'Schema', type: 'json', description: 'Result schema metadata', position: 'right' },
  ],

  configFields: [
    {
      id: 'query',
      label: 'SQL Query',
      type: 'code',
      default: 'SELECT * FROM data',
      required: true,
      description:
        'SQL query to execute against the input data. Supports {{node.field}} variables from upstream nodes.',
    },
    {
      id: 'tableName',
      label: 'Table Name',
      type: 'text',
      default: 'data',
      description: 'Name to assign the input data as a table',
    },
    {
      id: 'enableExtensions',
      label: 'Enable Extensions',
      type: 'toggle',
      default: false,
      description: 'Enable DuckDB extensions (httpfs, parquet, etc.)',
    },
    {
      id: 'maxRows',
      label: 'Max Rows',
      type: 'number',
      default: 10000,
      min: 1,
      max: 1000000,
      description: 'Maximum rows to return',
    },
    {
      id: 'outputColumns',
      label: 'Output Columns',
      type: 'text',
      default: '',
      description:
        'Comma-separated result columns (e.g. "id, name"). Lets downstream nodes reference them as {{this_node.column}}.',
    },
  ],

  validation: [
    {
      id: 'query_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.query,
        reason: 'SQL query is required',
      }),
    },
  ],
};

// ── EAV Operation ──
export const eavOperationSchema: StepSchema = {
  schemaId: 'stepflow:data:eav',
  name: 'EAV Operation',
  category: 'data',
  description:
    "Entity-Attribute-Value operations for flexible data modeling. Inside a Map loop, row columns are picked from the parent flow's data source.",
  icon: 'git-branch',
  color: '#3B82F6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['eav', 'entity', 'attribute', 'value', 'flexible-schema'],
  nodeComponent: 'data',

  inputs: [
    { id: 'input_entity', label: 'Entity', type: 'json', optional: false, position: 'left' },
    { id: 'input_attributes', label: 'Attributes', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'EAV operation result', position: 'right' },
    { id: 'output_metadata', label: 'Metadata', type: 'json', description: 'Operation metadata', position: 'right' },
  ],

  configFields: [
    {
      id: 'operation',
      label: 'Operation',
      type: 'dropdown',
      default: 'read',
      options: [
        { label: 'Read', value: 'read' },
        { label: 'Write', value: 'write' },
        { label: 'Update', value: 'update' },
        { label: 'Delete', value: 'delete' },
      ],
      required: true,
      description: 'EAV operation to perform',
    },
    {
      id: 'entityType',
      label: 'Entity Type',
      type: 'text',
      required: true,
      description: 'Type of entity (e.g., "customer", "order")',
    },
    {
      id: 'attributeFilter',
      label: 'Attribute Filter',
      type: 'text',
      default: '',
      description: 'Filter attributes by pattern (e.g., "address.*")',
    },
    {
      id: 'rowPath',
      label: 'Row Path',
      type: 'text',
      default: '$[0]',
      condition: (data) => data.configuration?.operation === 'read' && !!data.inMapLoop,
      description: 'JSONPath of the row to read inside a Map iteration (default is the current item).',
    },
    {
      id: 'columnSelector',
      label: 'Columns',
      type: 'text',
      default: '',
      condition: (data) => !!data.inMapLoop,
      description: 'Comma-separated column names to select, or empty for all columns of the row.',
    },
    {
      id: 'entityTypeMapping',
      label: 'Entity Type Mapping',
      type: 'text',
      default: '',
      condition: (data) => !!data.inMapLoop,
      description: 'Optional mapping from row column names to EAV entity types.',
    },
    {
      id: 'enableVersioning',
      label: 'Enable Versioning',
      type: 'toggle',
      default: false,
      description: 'Track attribute changes over time',
    },
  ],

  validation: [
    {
      id: 'entityType_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.entityType,
        reason: 'Entity type is required',
      }),
    },
  ],
};
