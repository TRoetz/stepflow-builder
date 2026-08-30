import { StepSchema, NodeData } from '@schema-types/schema';

// ── Sub-Flow Call Node ──
export const subFlowSchema: StepSchema = {
  schemaId: 'stepflow:subflow:invoke',
  name: 'Sub-Flow Call',
  category: 'subflow',
  description: 'Invoke another StepFlow workflow as a sub-flow with input/output mapping.',
  icon: 'folder-git',
  color: '#06B6D4',
  version: '1.0.0',
  isTemplate: true,
  tags: ['subflow', 'invoke', 'nested', 'composition', 'hierarchical'],
  nodeComponent: 'subflow',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
    { id: 'input_config', label: 'Config Override', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'Sub-flow output', position: 'right' },
    { id: 'output_status', label: 'Status', type: 'json', description: 'Execution status and metadata', position: 'right' },
    { id: 'output_errors', label: 'Errors', type: 'array', description: 'Any errors from sub-flow', position: 'right' },
  ],

  configFields: [
    {
      id: 'targetFlowId',
      label: 'Target Flow',
      type: 'dropdown',
      default: '',
      options: [], // Populated dynamically from registered flows
      required: true,
      description: 'Select the target flow to invoke',
    },
    {
      id: 'targetFlowVersion',
      label: 'Flow Version',
      type: 'dropdown',
      default: 'latest',
      options: [
        { label: 'Latest', value: 'latest' },
        { label: 'Pinned (specify)', value: 'pinned' },
      ],
      description: 'Version of the target flow',
    },
    {
      id: 'pinnedVersion',
      label: 'Pinned Version',
      type: 'text',
      default: '',
      condition: (data: NodeData) => data.configuration?.targetFlowVersion === 'pinned',
      description: 'Specific version to pin',
    },
    {
      id: 'inputMapping',
      label: 'Input Mapping',
      type: 'json',
      default: JSON.stringify({ 'input': '$.input_data' }, null, 2),
      required: true,
      description: 'Map this flow\'s inputs to the sub-flow\'s inputs (JSONPath)',
    },
    {
      id: 'outputMapping',
      label: 'Output Mapping',
      type: 'json',
      default: JSON.stringify({ 'result': '$.output_result' }, null, 2),
      required: true,
      description: 'Map sub-flow outputs to this flow\'s outputs (JSONPath)',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 60000,
      min: 1000,
      max: 300000,
      description: 'Maximum time for sub-flow execution',
    },
    {
      id: 'errorMode',
      label: 'Error Handling',
      type: 'dropdown',
      default: 'propagate',
      options: [
        { label: 'Propagate Error', value: 'propagate' },
        { label: 'Continue with Default', value: 'continue_with_default' },
        { label: 'Retry', value: 'retry' },
      ],
      description: 'How to handle sub-flow errors',
    },
    {
      id: 'retryCount',
      label: 'Retry Count',
      type: 'number',
      default: 0,
      min: 0,
      max: 5,
      condition: (data: NodeData) => data.configuration?.errorMode === 'retry',
      description: 'Number of retry attempts on sub-flow failure',
    },
    {
      id: 'retryDelay',
      label: 'Retry Delay (ms)',
      type: 'number',
      default: 5000,
      min: 1000,
      max: 60000,
      condition: (data: NodeData) => data.configuration?.errorMode === 'retry',
      description: 'Delay between retry attempts',
    },
    {
      id: 'enableLogging',
      label: 'Enable Logging',
      type: 'toggle',
      default: true,
      description: 'Log sub-flow execution details',
    },
  ],

  validation: [
    {
      id: 'targetFlowId_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.targetFlowId,
        reason: 'Target flow is required',
      }),
    },
    {
      id: 'inputMapping_valid_json',
      check: (data: NodeData) => {
        try {
          JSON.parse(data.configuration?.inputMapping as string);
          return { isValid: true };
        } catch {
          return {
            isValid: false,
            reason: 'Input mapping must be valid JSON',
          };
        }
      },
    },
    {
      id: 'outputMapping_valid_json',
      check: (data: NodeData) => {
        try {
          JSON.parse(data.configuration?.outputMapping as string);
          return { isValid: true };
        } catch {
          return {
            isValid: false,
            reason: 'Output mapping must be valid JSON',
          };
        }
      },
    },
    {
      id: 'timeout_valid',
      check: (data: NodeData) => ({
        isValid:
          typeof data.configuration?.timeout === 'number' &&
          (data.configuration.timeout as number) > 0,
        reason: 'Timeout must be a positive number',
      }),
    },
  ],
};
