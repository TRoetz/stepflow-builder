import { StepSchema } from '@schema-types/schema';

// ── START State ──
export const startStateSchema: StepSchema = {
  schemaId: 'stepflow:terminal:start',
  name: 'START',
  category: 'terminal',
  description: 'Marks the beginning of the workflow. Every flow starts here.',
  icon: 'play',
  color: '#10B981',
  version: '1.0.0',
  isTemplate: false,
  tags: ['start', 'begin', 'entry', 'terminal'],
  nodeComponent: 'terminal',

  inputs: [],

  outputs: [
    { id: 'start_output', label: 'Start', type: 'any', description: 'Flow entry point', position: 'right' },
  ],

  configFields: [
    {
      id: 'description',
      label: 'Description',
      type: 'textarea',
      default: '',
      description: 'Describe what this flow does',
    },
  ],

  validation: [],
};

// ── END State ──
export const endStateSchema: StepSchema = {
  schemaId: 'stepflow::end',
  name: 'END',
  category: 'terminal',
  description: 'Marks the end of the workflow. Place one or more END states to define flow completion.',
  icon: 'stop',
  color: '#EF4444',
  version: '1.0.0',
  isTemplate: false,
  tags: ['end', 'finish', 'exit', 'terminal'],
  nodeComponent: 'terminal',

  inputs: [
    { id: 'end_input', label: 'End', type: 'any', optional: false, position: 'left' },
  ],

  outputs: [],

  configFields: [
    {
      id: 'description',
      label: 'Description',
      type: 'textarea',
      default: '',
      description: 'Describe the expected outcome at this endpoint',
    },
  ],

  validation: [],
};
