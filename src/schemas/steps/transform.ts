import { StepSchema, NodeData } from '@schema-types/schema';

// ── JSONata Processor ──
export const jsonataSchema: StepSchema = {
  schemaId: 'stepflow:transform:jsonata',
  name: 'JSONata Processor',
  category: 'transform',
  description: 'Transform data using JSONata expressions.',
  icon: 'code',
  color: '#EC4899',
  version: '1.0.0',
  isTemplate: true,
  tags: ['jsonata', 'transform', 'expression', 'data-mapping'],
  nodeComponent: 'transform',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'Transformed data', position: 'right' },
    { id: 'output_error', label: 'Error', type: 'string', description: 'Expression error if any', position: 'right' },
  ],

  configFields: [
    {
      id: 'expression',
      label: 'JSONata Expression',
      type: 'code',
      default: '$map($$.input, function($v) { $v.processed = true; $v })',
      required: true,
      description: 'JSONata expression to transform the input data',
    },
    {
      id: 'enableDebug',
      label: 'Enable Debug',
      type: 'toggle',
      default: false,
      description: 'Enable detailed debug output',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 5000,
      min: 100,
      max: 60000,
      description: 'Maximum time for expression evaluation',
    },
  ],

  validation: [
    {
      id: 'expression_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.expression,
        reason: 'JSONata expression is required',
      }),
    },
  ],
};

// ── Script Execution ──
export const scriptSchema: StepSchema = {
  schemaId: 'stepflow:transform:script',
  name: 'Script Execution',
  category: 'transform',
  description: 'Execute custom scripts (JavaScript/Python) to transform data.',
  icon: 'terminal',
  color: '#EC4899',
  version: '1.0.0',
  isTemplate: true,
  tags: ['script', 'javascript', 'python', 'transform', 'custom'],
  nodeComponent: 'transform',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_env', label: 'Environment', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'Script output', position: 'right' },
    { id: 'output_logs', label: 'Logs', type: 'array', description: 'Script execution logs', position: 'right' },
    { id: 'output_error', label: 'Error', type: 'string', description: 'Script error if any', position: 'right' },
  ],

  configFields: [
    {
      id: 'language',
      label: 'Language',
      type: 'dropdown',
      default: 'javascript',
      options: [
        { label: 'JavaScript (Node.js)', value: 'javascript' },
        { label: 'Python', value: 'python' },
        { label: 'PowerShell', value: 'powershell' },
      ],
      required: true,
      description: 'Scripting language to use',
    },
    {
      id: 'script',
      label: 'Script',
      type: 'code',
      default: '// Access input via context.input\n// Return transformed data\nreturn { ...context.input, processed: true };',
      required: true,
      description: 'Script code to execute',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 10000,
      min: 1000,
      max: 120000,
      description: 'Maximum execution time',
    },
    {
      id: 'enableSandbox',
      label: 'Enable Sandbox',
      type: 'toggle',
      default: true,
      description: 'Run script in a sandboxed environment',
    },
    {
      id: 'maxMemory',
      label: 'Max Memory (MB)',
      type: 'number',
      default: 256,
      min: 64,
      max: 2048,
      description: 'Maximum memory for script execution',
    },
  ],

  validation: [
    {
      id: 'script_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.script,
        reason: 'Script code is required',
      }),
    },
    {
      id: 'language_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.language,
        reason: 'Scripting language is required',
      }),
    },
  ],
};
