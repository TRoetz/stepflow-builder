import { StepSchema } from '@schema-types/schema';

// ── SSH Command ──
export const sshCommandSchema: StepSchema = {
  schemaId: 'stepflow:ssh:command',
  name: 'SSH Command',
  category: 'remote',
  description:
    "Connect to a curated remote host and execute a command. The command is AI-safety-checked before execution; enable Override to bypass.",
  icon: '',
  color: '#F59E0B',
  version: '1.0.0',
  isTemplate: true,
  tags: ['ssh', 'remote', 'command'],
  nodeComponent: 'remote',

  inputs: [
    { id: 'input', label: 'Input (command text)', type: 'string', optional: true, position: 'left' },
  ],

  outputs: [
    {
      id: 'output',
      label: 'Output {host, command, exitCode, stdout, stderr}',
      type: 'json',
      description: 'SSH execution result',
      position: 'right',
      fields: [
        { name: 'host', type: 'string' },
        { name: 'command', type: 'string' },
        { name: 'exitCode', type: 'number' },
        { name: 'stdout', type: 'string' },
        { name: 'stderr', type: 'string' },
      ],
    },
  ],

  configFields: [
    {
      id: 'host',
      label: 'Host Name',
      type: 'text',
      required: true,
      description: "Curated host name from ssh_hosts.json (resource URI becomes ssh://<name>)",
    },
    {
      id: 'command',
      label: 'Command (static)',
      type: 'textarea',
      default: '',
      description:
        'Optional static command. If empty and an upstream node is connected, the incoming input text is executed instead.',
    },
    {
      id: 'override',
      label: 'Override Safety Check',
      type: 'toggle',
      default: false,
      description: 'Bypass the AI harmful-command check and issue any command',
    },
    {
      id: 'timeoutSeconds',
      label: 'Timeout (seconds)',
      type: 'number',
      default: 30,
      min: 1,
      max: 600,
      description: 'SSH connect/execute timeout in seconds',
    },
  ],

  validation: [
    {
      id: 'host_required',
      check: (data) => ({
        isValid: !!(data.configuration?.host as string | undefined)?.trim(),
        reason: 'Host Name is required (curated host from ssh_hosts.json)',
      }),
    },
  ],
};
