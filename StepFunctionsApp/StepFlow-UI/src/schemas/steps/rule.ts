import { StepSchema, NodeData } from '@schema-types/schema';

// ── Rule Engine ──
export const ruleEngineSchema: StepSchema = {
  schemaId: 'stepflow:rule:rule_engine',
  name: 'Rule Engine',
  category: 'rule',
  description: 'Evaluate business rules against input data to determine outcomes.',
  icon: 'scale',
  color: '#F59E0B',
  version: '1.0.0',
  isTemplate: true,
  tags: ['rule', 'engine', 'decision', 'business-logic'],
  nodeComponent: 'rule',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'Rule evaluation result', position: 'right' },
    { id: 'output_matched', label: 'Matched Rules', type: 'array', description: 'List of matched rule IDs', position: 'right' },
  ],

  configFields: [
    {
      id: 'ruleSet',
      label: 'Rule Set',
      type: 'code',
      default: JSON.stringify([{ name: 'default', expression: 'true', outcome: 'pass' }], null, 2),
      required: true,
      description: 'JSON array of rules with expressions and outcomes',
    },
    {
      id: 'evaluationMode',
      label: 'Evaluation Mode',
      type: 'dropdown',
      default: 'first_match',
      options: [
        { label: 'First Match', value: 'first_match' },
        { label: 'All Match', value: 'all_match' },
        { label: 'Highest Priority', value: 'highest_priority' },
      ],
      description: 'How to evaluate multiple matching rules',
    },
    {
      id: 'defaultOutcome',
      label: 'Default Outcome',
      type: 'text',
      default: 'pass',
      description: 'Outcome when no rules match',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 5000,
      min: 100,
      max: 60000,
      description: 'Maximum time for rule evaluation',
    },
    {
      id: 'enableLogging',
      label: 'Enable Logging',
      type: 'toggle',
      default: false,
      description: 'Log rule evaluation details',
    },
  ],

  validation: [
    {
      id: 'ruleSet_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.ruleSet,
        reason: 'Rule set is required',
      }),
    },
    {
      id: 'ruleSet_valid_json',
      check: (data: NodeData) => {
        try {
          const parsed = JSON.parse(data.configuration?.ruleSet as string);
          return {
            isValid: Array.isArray(parsed) && parsed.length > 0,
            reason: 'Rule set must be a non-empty JSON array',
          };
        } catch {
          return {
            isValid: false,
            reason: 'Rule set must be valid JSON',
          };
        }
      },
    },
  ],
};

// ── Microsoft RulesEngine ──
export const msRulesEngineSchema: StepSchema = {
  schemaId: 'stepflow:rule:ms-rules',
  name: 'MS RulesEngine',
  category: 'rule',
  description: 'Evaluate rules using Microsoft RulesEngine with C# expression support.',
  icon: 'shield-check',
  color: '#F59E0B',
  version: '1.0.0',
  isTemplate: true,
  tags: ['rule', 'engine', 'microsoft', 'csharp', 'business-logic'],
  nodeComponent: 'rule',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_workflows', label: 'Workflows', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'RulesEngine evaluation result', position: 'right' },
    { id: 'output_errors', label: 'Errors', type: 'array', description: 'Any rule evaluation errors', position: 'right' },
  ],

  configFields: [
    {
      id: 'ruleName',
      label: 'Rule Name',
      type: 'text',
      required: true,
      description: 'Name of the rule to evaluate',
    },
    {
      id: 'workflowName',
      label: 'Workflow Name',
      type: 'text',
      required: true,
      description: 'Name of the workflow containing the rule',
    },
    {
      id: 'rulesDefinition',
      label: 'Rules Definition',
      type: 'code',
      default: JSON.stringify({
        WorkflowName: 'default',
        Rules: [
          {
            Key: 'default_rule',
            Expression: 'input => true',
            SuccessEvent: { LocalExecutionEventName: 'pass' },
          },
        ],
      }, null, 2),
      required: true,
      description: 'JSON rules definition following MS RulesEngine format',
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
      default: 10000,
      min: 100,
      max: 60000,
      description: 'Maximum time for rule evaluation',
    },
  ],

  validation: [
    {
      id: 'ruleName_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.ruleName,
        reason: 'Rule name is required',
      }),
    },
    {
      id: 'workflowName_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.workflowName,
        reason: 'Workflow name is required',
      }),
    },
    {
      id: 'rulesDefinition_valid_json',
      check: (data: NodeData) => {
        try {
          JSON.parse(data.configuration?.rulesDefinition as string);
          return { isValid: true };
        } catch {
          return {
            isValid: false,
            reason: 'Rules definition must be valid JSON',
          };
        }
      },
    },
  ],
};
