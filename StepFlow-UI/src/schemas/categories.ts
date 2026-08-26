import { CategoryDefinition, StepCategory } from '@schema-types/schema';

// -- 13 Category Definitions --
export const categoryDefinitions: CategoryDefinition[] = [
  {
    id: 'terminal',
    name: 'Terminal',
    description: 'START and END states that mark the boundaries of your workflow',
    icon: '⚡',
    color: '#10B981',
    nodeComponent: 'TerminalNode',
  },
  {
    id: 'flow',
    name: 'Flow Control',
    description: 'Flow states that control execution path: Choice, Map, Parallel, Succeed, Fail',
    icon: '🔀',
    color: '#8B5CF6',
    nodeComponent: 'FlowNode',
  },
  {
    id: 'ai',
    name: 'AI & LLM',
    description: 'AI and Large Language Model operations for decision-making and text generation',
    icon: '🤖',
    color: '#8B5CF6',
    nodeComponent: 'AiNode',
  },
  {
    id: 'rule',
    name: 'Rule Engine',
    description: 'Business rule evaluation and decision logic',
    icon: '⚖️',
    color: '#F59E0B',
    nodeComponent: 'RuleNode',
  },
  {
    id: 'data',
    name: 'Data',
    description: 'Data access, querying, and transformation operations',
    icon: '📊',
    color: '#3B82F6',
    nodeComponent: 'DataNode',
  },
  {
    id: 'api',
    name: 'API',
    description: 'HTTP requests and registered API integrations',
    icon: '🔌',
    color: '#10B981',
    nodeComponent: 'ApiNode',
  },
  {
    id: 'transform',
    name: 'Transform',
    description: 'Data transformation, scripting, and expression evaluation',
    icon: '⚙️',
    color: '#EC4899',
    nodeComponent: 'TransformNode',
  },
  {
    id: 'utility',
    name: 'Utility',
    description: 'Utility operations like pass-through, wait, and branching',
    icon: '🔧',
    color: '#6B7280',
    nodeComponent: 'UtilityNode',
  },
  {
    id: 'subflow',
    name: 'Sub-Flow',
    description: 'Invoke other StepFlow workflows as first-class nodes',
    icon: '📦',
    color: '#06B6D4',
    nodeComponent: 'SubFlowNode',
  },
  {
    id: 'human',
    name: 'Human Tasks',
    description: 'Suspend the flow until a person completes an external action (approval, review, file drop)',
    icon: '👤',
    color: '#FB923C',
    nodeComponent: 'HumanTaskNode',
  },
  {
    id: 'formcapture',
    name: 'Form Capture',
    description: 'Suspend the flow until a person submits a JSON-configured form (UIData page bound to an AttributeDomain)',
    icon: '📝',
    color: '#22C55E',
    nodeComponent: 'FormCaptureNode',
  },
  {
    id: 'remote',
    name: 'Remote',
    description: 'Execute actions on remote hosts over SSH',
    icon: '',
    color: '#F59E0B',
    nodeComponent: 'SshNode',
  },
  {
    id: 'transfer',
    name: 'File Transfer',
    description: 'Fetch files from remote hosts via SCP, SFTP, FTP or XCOPY (SMB)',
    icon: '',
    color: '#0EA5E9',
    nodeComponent: 'FetchFilesNode',
  },
];

// ── Lookup Maps ──
export const categoryById = new Map<StepCategory, CategoryDefinition>(
  categoryDefinitions.map((c) => [c.id, c])
);

export const categoryColorById = new Map<StepCategory, string>(
  categoryDefinitions.map((c) => [c.id, c.color])
);

export const categoryNodeComponentMap: Record<StepCategory, string> = {
  terminal: 'TerminalNode',
  flow: 'FlowNode',
  ai: 'AiNode',
  rule: 'RuleNode',
  data: 'DataNode',
  api: 'ApiNode',
  transform: 'TransformNode',
  utility: 'UtilityNode',
  subflow: 'SubFlowNode',
  human: 'HumanTaskNode',
  formcapture: 'FormCaptureNode',
  remote: 'SshNode',
  transfer: 'FetchFilesNode',
};
