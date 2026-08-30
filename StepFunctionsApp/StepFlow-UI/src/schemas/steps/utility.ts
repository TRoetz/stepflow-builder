import { StepSchema, NodeData } from '@schema-types/schema';

// ── Pass Through ──
export const passThroughSchema: StepSchema = {
  schemaId: 'stepflow:utility:pass',
  name: 'Pass Through',
  category: 'utility',
  description:
    'Forwards input data to its output unchanged — a named checkpoint for routing & debugging (exports as an ASL "Pass" state). Enable logging to capture the exact payload in this node\'s execution log.',
  icon: 'arrow-right',
  color: '#6B7280',
  version: '1.0.0',
  isTemplate: true,
  tags: ['pass-through', 'routing', 'debug', 'identity'],
  nodeComponent: 'utility',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_data', label: 'Output', type: 'any', description: 'Unmodified input data', position: 'right' },
  ],

  configFields: [
    {
      id: 'label',
      label: 'Label',
      type: 'text',
      default: '',
      description: 'Optional label for identification',
    },
    {
      id: 'enableLogging',
      label: 'Enable Logging',
      type: 'toggle',
      default: false,
      description:
        'Capture the exact payload in this node\'s execution log (Info tab) and browser console during simulation',
    },
  ],

  validation: [],
};

// ── Wait ──
export const waitSchema: StepSchema = {
  schemaId: 'stepflow:utility:wait',
  name: 'Wait',
  category: 'utility',
  description: 'Pause execution for a specified duration.',
  icon: 'clock',
  color: '#6B7280',
  version: '1.0.0',
  isTemplate: true,
  tags: ['wait', 'delay', 'pause', 'timing'],
  nodeComponent: 'utility',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_data', label: 'Output', type: 'any', description: 'Input data after wait', position: 'right' },
    { id: 'output_timestamp', label: 'Timestamp', type: 'string', description: 'Timestamp after wait', position: 'right' },
  ],

  configFields: [
    {
      id: 'duration',
      label: 'Duration (seconds)',
      type: 'number',
      default: 5,
      min: 0,
      max: 3600,
      required: true,
      description: 'Duration to wait in seconds',
    },
    {
      id: 'durationUnit',
      label: 'Duration Unit',
      type: 'dropdown',
      default: 'seconds',
      options: [
        { label: 'Seconds', value: 'seconds' },
        { label: 'Minutes', value: 'minutes' },
        { label: 'Hours', value: 'hours' },
      ],
      description: 'Unit for the duration',
    },
    {
      id: 'enableLogging',
      label: 'Enable Logging',
      type: 'toggle',
      default: false,
      description: 'Log wait start and end times',
    },
  ],

  validation: [
    {
      id: 'duration_required',
      check: (data: NodeData) => ({
        isValid: typeof data.configuration?.duration === 'number' && (data.configuration.duration as number) >= 0,
        reason: 'Duration must be a non-negative number',
      }),
    },
  ],
};

// ── Branch ──
export const branchSchema: StepSchema = {
  schemaId: 'stepflow:utility:branch',
  name: 'Branch',
  category: 'utility',
  description: 'Branch execution based on a condition. Routes data to different paths.',
  icon: 'git-branch',
  color: '#6B7280',
  version: '1.0.0',
  isTemplate: true,
  tags: ['branch', 'conditional', 'routing', 'if-else'],
  nodeComponent: 'utility',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
    { id: 'input_condition', label: 'Condition', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_true', label: 'True Path', type: 'any', description: 'Output when condition is true', position: 'right' },
    { id: 'output_false', label: 'False Path', type: 'any', description: 'Output when condition is false', position: 'right' },
    { id: 'output_both', label: 'Both Paths', type: 'any', description: 'Output to both paths (when enabled)', position: 'bottom' },
  ],

  configFields: [
    {
      id: 'condition',
      label: 'Condition',
      type: 'code',
      default: 'context.input.someField === true',
      required: true,
      description: 'Expression to evaluate for branching',
    },
    {
      id: 'branchMode',
      label: 'Branch Mode',
      type: 'dropdown',
      default: 'exclusive',
      options: [
        { label: 'Exclusive (if-else)', value: 'exclusive' },
        { label: 'Both Paths', value: 'both' },
      ],
      description: 'How to route data',
    },
    {
      id: 'enableLogging',
      label: 'Enable Logging',
      type: 'toggle',
      default: false,
      description: 'Log branch decisions',
    },
  ],

  validation: [
    {
      id: 'condition_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.condition,
        reason: 'Condition expression is required',
      }),
    },
  ],
};
