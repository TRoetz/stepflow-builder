import { StepSchema } from '@schema-types/schema';

// ── Human Task State ──
export const humanTaskSchema: StepSchema = {
  schemaId: 'stepflow:human:task',
  name: 'Human Task',
  category: 'human',
  description:
    "Suspend the flow until a person completes an external action (approval, review, file drop). Completion arrives via POST /api/human-tasks/{id}/complete or by dropping {taskId}.json into the watched directory.",
  icon: 'user-check',
  color: '#FB923C',
  version: '1.0.0',
  isTemplate: true,
  tags: ['human', 'approval', 'manual', 'durable', 'wait'],
  nodeComponent: 'human',
  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'any', optional: true, position: 'left' },
  ],
  outputs: [
    {
      id: 'output_data',
      label: 'Completion Result',
      type: 'json',
      position: 'right',
      description: "The human's result (placed at Result Path, or replacing the input when unset)",
    },
  ],
  configFields: [
    { id: 'taskTitle', label: 'Task Title', type: 'text', required: true, description: 'Shown to the assignee in the task list.' },
    { id: 'assignee', label: 'Assignee / Team', type: 'text', description: 'Who should handle this (free-form).' },
    {
      id: 'completionMethod',
      label: 'Completion Method',
      type: 'dropdown',
      default: 'api',
      options: [
        { label: 'API — POST /api/human-tasks/{id}/complete', value: 'api' },
        { label: 'File drop — watch a directory for {taskId}.json', value: 'file' },
      ],
    },
    { id: 'watchDirectory', label: 'Watch Directory (file mode)', type: 'text', condition: (n) => n.configuration?.completionMethod === 'file', description: 'Defaults to human-task-completions.' },
    { id: 'resultPath', label: 'Result Path', type: 'text', description: "Dotted path where the completion result is placed in the flow input. Empty = replace entire input." },
  ],
  validation: [
    {
      id: 'taskTitle_required',
      check: (data) => ({
        isValid: !!data.configuration?.taskTitle,
        reason: 'Task Title is required',
      }),
    },
  ],
};
