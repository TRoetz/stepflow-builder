import { StepSchema } from '@schema-types/schema';

// -- Fetch Remote Files --
export const fetchFilesSchema: StepSchema = {
  schemaId: 'stepflow:fetch:files',
  name: 'Fetch Remote Files',
  category: 'transfer',
  description:
    "Fetch files from a curated remote host via SCP, SFTP, FTP/FTPS or XCOPY (SMB). Wildcards (* ?) are supported where the protocol allows; files land in the local destination directory.",
  icon: '',
  color: '#0EA5E9',
  version: '1.0.0',
  isTemplate: true,
  tags: ['scp', 'sftp', 'ftp', 'xcopy', 'remote', 'files'],
  nodeComponent: 'transfer',

  inputs: [
    { id: 'input', label: 'Input (optional)', type: 'any', optional: true, position: 'left' },
  ],

  outputs: [
    {
      id: 'output',
      label: 'Output {host, protocol, destDir, files[], fileCount}',
      type: 'json',
      description: 'File transfer result',
      position: 'right',
      fields: [
        { name: 'host', type: 'string' },
        { name: 'protocol', type: 'string' },
        { name: 'sourcePath', type: 'string' },
        { name: 'destDir', type: 'string' },
        { name: 'fileCount', type: 'number' },
      ],
    },
  ],

  configFields: [
    {
      id: 'host',
      label: 'Host Name',
      type: 'text',
      required: true,
      description: "Curated host name from ssh_hosts.json (resource URI becomes fetch://<name>?proto=<protocol>)",
    },
    {
      id: 'protocol',
      label: 'Protocol',
      type: 'dropdown',
      default: 'scp',
      options: [
        { label: 'SCP', value: 'scp' },
        { label: 'SFTP', value: 'sftp' },
        { label: 'FTP/FTPS', value: 'ftp' },
        { label: 'XCOPY (SMB)', value: 'xcopy' },
      ],
      description: 'Transfer protocol. XCOPY requires a Windows host with an SMB Share configured in ssh_hosts.json.',
    },
    {
      id: 'sourcePath',
      label: 'Source Path',
      type: 'text',
      required: true,
      description: "Remote path or wildcard pattern (* ?). SCP does not support wildcards.",
    },
    {
      id: 'destDir',
      label: 'Destination Directory',
      type: 'text',
      required: true,
      description: 'Local directory where fetched files are written (created if missing)',
    },
    {
      id: 'timeoutSeconds',
      label: 'Timeout (seconds)',
      type: 'number',
      default: 120,
      min: 30,
      max: 600,
      description: 'Transfer timeout in seconds',
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
    {
      id: 'sourcePath_required',
      check: (data) => ({
        isValid: !!(data.configuration?.sourcePath as string | undefined)?.trim(),
        reason: 'Source Path is required (remote path or wildcard pattern)',
      }),
    },
    {
      id: 'destDir_required',
      check: (data) => ({
        isValid: !!(data.configuration?.destDir as string | undefined)?.trim(),
        reason: 'Destination Directory is required (local directory for fetched files)',
      }),
    },
  ],
};
