import { StepSchema } from '@schema-types/schema';

// -- Form Capture State --
export const formCaptureSchema: StepSchema = {
  schemaId: 'stepflow:formcapture:capture',
  name: 'Form Capture',
  category: 'formcapture',
  description:
    "Suspend the flow until a person submits a JSON-configured form (UIData page). Submissions are validated against the bound AttributeDomain and persisted as EAV rows.",
  icon: '📝',
  color: '#22C55E',
  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'any', optional: true, position: 'left' },
  ],
  outputs: [
    {
      id: 'captured_data',
      label: 'Captured Data',
      type: 'json',
      position: 'right',
      description: "The submitted values (placed at Result Path, or replacing the input when unset)",
    },
  ],
  configFields: [
    {
      id: 'formId',
      label: 'Form ID',
      type: 'dropdown',
      required: true,
      description: 'The JSON form (UIData page) to present when this state suspends. Manage forms in the Form Builder window.',
      validate: (v) => (v ? null : 'Form ID is required'),
    },
    {
      id: 'formVersion',
      label: 'Form Version',
      type: 'dropdown',
      description: 'Pin a specific published version of the form. Empty = follow the current (latest) version.',
    },
    {
      id: 'title',
      label: 'Task Title',
      type: 'text',
      description: "Shown on the standalone fill-in page and in the human-tasks list. Defaults to the form's title.",
    },
    {
      id: 'assignee',
      label: 'Assignee',
      type: 'text',
      description: 'Optional person or team responsible for filling this form.',
    },
    {
      id: 'resultPath',
      label: 'Result Path',
      type: 'text',
      description: "Dotted path where the captured values are placed in the flow input. Empty = replace entire input.",
    },
  ],
  validation: [
    {
      id: 'formId_required',
      check: (nodeData) => ({
        isValid: Boolean(nodeData.configuration?.formId),
        reason: 'Form ID is required',
      }),
    },
  ],
  version: '1.0.0',
  isTemplate: true,
  tags: ['formcapture', 'capture', 'human', 'durable'],
  nodeComponent: 'formcapture',
};
