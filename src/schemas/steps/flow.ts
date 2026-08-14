import { StepSchema } from '@schema-types/schema';

// ── Choice State ──
export const choiceStateSchema: StepSchema = {
  schemaId: 'stepflow:flow:choice',
  name: 'Choice',
  category: 'flow',
  description: 'Conditional branching based on data comparisons. Route execution down different paths depending on conditions.',
  icon: 'git-branch',
  color: '#8B5CF6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['choice', 'branch', 'conditional', 'if-else', 'routing'],
  nodeComponent: 'flow',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_true', label: 'True', type: 'any', description: 'When condition matches', position: 'right' },
    { id: 'output_false', label: 'False', type: 'any', description: 'When condition does not match', position: 'right' },
  ],

  configFields: [
    {
      id: 'condition',
      label: 'Condition',
      type: 'code',
      default: '',
      description: 'Expression that evaluates to true or false',
    },
    {
      id: 'defaultOutput',
      label: 'Default Output',
      type: 'dropdown',
      default: 'output_false',
      options: [{ label: 'True', value: 'output_true' }, { label: 'False', value: 'output_false' }],
      description: 'Output to use when condition cannot be evaluated',
    },
  ],

  validation: [],
};

// ── Map State ──
export const mapStateSchema: StepSchema = {
  schemaId: 'stepflow:flow:map',
  name: 'Map',
  category: 'flow',
  description: 'Iterate over a dataset and run a sub-workflow for each item. Process collections in sequence.',
  icon: 'layers',
  color: '#8B5CF6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['map', 'iterate', 'loop', 'collection', 'for-each'],
  nodeComponent: 'flow',

  inputs: [
    { id: 'input_items', label: 'Items', type: 'array', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_results', label: 'Results', type: 'array', description: 'Array of results from each iteration', position: 'right' },
  ],

  configFields: [
    {
      id: 'targetFlowId',
      label: 'Iterator Flow (Sub-Flow)',
      type: 'dropdown',
      default: '',
      options: [], // Populated dynamically from registered flows
      required: true,
      description: 'Select the sub-flow to execute as the iterator for each item',
    },
    {
      id: 'itemsPath',
      label: 'Items Path',
      type: 'text',
      default: '$.items',
      description: 'JSONPath expression pointing to the array to iterate over',
    },
    {
      id: 'maxConcurrency',
      label: 'Max Concurrency',
      type: 'number',
      default: 1,
      description: 'Maximum number of parallel iterations (1 = sequential)',
    },
    {
      id: 'resultPath',
      label: 'Result Path',
      type: 'text',
      default: '$.results',
      description: 'Path to store the collected results',
    },
  ],

  validation: [],
};

// ── Parallel State ──
export const parallelStateSchema: StepSchema = {
  schemaId: 'stepflow:flow:parallel',
  name: 'Parallel',
  category: 'flow',
  description: 'Spawn independent branches that run concurrently. All branches must complete before proceeding.',
  icon: 'split-merge',
  color: '#8B5CF6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['parallel', 'concurrent', 'branches', 'fan-out', 'fan-in'],
  nodeComponent: 'flow',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [
    { id: 'output_merged', label: 'Merged', type: 'any', description: 'Combined results from all branches', position: 'right' },
  ],

  configFields: [
    {
      id: 'numBranches',
      label: 'Number of Branches',
      type: 'number',
      default: 2,
      description: 'Number of parallel branches to spawn',
    },
    {
      id: 'timeout',
      label: 'Timeout (seconds)',
      type: 'number',
      default: 300,
      description: 'Maximum time to wait for all branches to complete',
    },
    {
      id: 'failOnBranchFailure',
      label: 'Fail on Branch Failure',
      type: 'toggle',
      default: true,
      description: 'Stop all branches if any single branch fails',
    },
  ],

  validation: [],
};

// ── Succeed State ──
export const succeedStateSchema: StepSchema = {
  schemaId: 'stepflow:flow:succeed',
  name: 'Succeed',
  category: 'flow',
  description: 'Terminate the workflow successfully. Use to end a branch of execution with a success outcome.',
  icon: 'check-circle',
  color: '#10B981',
  version: '1.0.0',
  isTemplate: true,
  tags: ['succeed', 'success', 'terminate', 'complete'],
  nodeComponent: 'flow',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [],

  configFields: [
    {
      id: 'outputData',
      label: 'Output Data',
      type: 'json',
      default: '{}',
      description: 'Data to return as the workflow result',
    },
  ],

  validation: [],
};

// ── Fail State ──
export const failStateSchema: StepSchema = {
  schemaId: 'stepflow:flow:fail',
  name: 'Fail',
  category: 'flow',
  description: 'Terminate the workflow with an error. Use to signal failure conditions.',
  icon: 'x-circle',
  color: '#EF4444',
  version: '1.0.0',
  isTemplate: true,
  tags: ['fail', 'error', 'terminate', 'exception'],
  nodeComponent: 'flow',

  inputs: [
    { id: 'input_data', label: 'Input', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [],

  configFields: [
    {
      id: 'errorCause',
      label: 'Error Cause',
      type: 'text',
      default: 'Workflow failed',
      description: 'Description of why the workflow failed',
    },
    {
      id: 'errorMessage',
      label: 'Error Message',
      type: 'textarea',
      default: '',
      description: 'Detailed error message',
    },
  ],

  validation: [],
};
