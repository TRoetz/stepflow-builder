import { StepSchema, NodeData } from '@schema-types/schema';

// ── AI Decision ──
export const aiDecisionSchema: StepSchema = {
  schemaId: 'stepflow:ai:decision',
  name: 'AI Decision',
  category: 'ai',
  description: 'Use an AI/LLM model to make a decision based on input data.',
  icon: 'brain',
  color: '#8B5CF6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['ai', 'llm', 'decision', 'classification'],
  nodeComponent: 'ai',

  inputs: [
    { id: 'input_data', label: 'Input Data', type: 'json', optional: false, position: 'left' },
    { id: 'input_context', label: 'Context', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_result', label: 'Result', type: 'json', description: 'Full AI response', position: 'right' },
    { id: 'output_decision', label: 'Decision', type: 'boolean', description: 'Binary decision output', position: 'right' },
  ],

  configFields: [
    {
      id: 'model',
      label: 'Model',
      type: 'dropdown',
      default: 'gpt-4-turbo',
      options: [
        { label: 'GPT-4 Turbo', value: 'gpt-4-turbo' },
        { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
        { label: 'Claude 3', value: 'claude-3' },
        { label: 'Claude 3.5 Sonnet', value: 'claude-3.5-sonnet' },
      ],
      required: true,
      description: 'LLM model to use for decision making',
    },
    {
      id: 'prompt',
      label: 'Prompt',
      type: 'code',
      default: 'Analyze the input and provide a decision.',
      required: true,
      description: 'System prompt for the AI model',
    },
    {
      id: 'temperature',
      label: 'Temperature',
      type: 'slider',
      default: 0.7,
      min: 0,
      max: 2,
      description: 'Controls randomness (0 = deterministic, 2 = creative)',
    },
    {
      id: 'llmService',
      label: 'LLM Service',
      type: 'dropdown',
      default: 'azureOpenAI',
      options: [
        { label: 'Azure OpenAI', value: 'azureOpenAI' },
        { label: 'OpenAI', value: 'openAI' },
        { label: 'Anthropic', value: 'anthropic' },
      ],
      description: 'LLM provider service',
    },
    {
      id: 'outputFormat',
      label: 'Output Format',
      type: 'dropdown',
      default: 'json',
      options: [
        { label: 'JSON', value: 'json' },
        { label: 'Text', value: 'text' },
        { label: 'Markdown', value: 'markdown' },
      ],
      description: 'Format of the AI response',
    },
    {
      id: 'maxTokens',
      label: 'Max Tokens',
      type: 'number',
      default: 1000,
      min: 1,
      max: 4096,
      description: 'Maximum tokens in the response',
    },
  ],

  validation: [
    {
      id: 'model_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.model,
        reason: 'Model is required',
      }),
    },
    {
      id: 'prompt_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.prompt,
        reason: 'Prompt is required',
      }),
    },
    {
      id: 'temperature_range',
      check: (data: NodeData) => ({
        isValid:
          typeof data.configuration?.temperature === 'number' &&
          (data.configuration.temperature as number) >= 0 &&
          (data.configuration.temperature as number) <= 2,
        reason: 'Temperature must be between 0 and 2',
      }),
    },
    {
      id: 'llmService_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.llmService,
        reason: 'LLM service is required',
      }),
    },
  ],
};

// ── AI Text Generation ──
export const aiTextSchema: StepSchema = {
  schemaId: 'stepflow:ai:text',
  name: 'AI Text Generation',
  category: 'ai',
  description: 'Generate text content using an AI/LLM model.',
  icon: 'pen-tool',
  color: '#8B5CF6',
  version: '1.0.0',
  isTemplate: true,
  tags: ['ai', 'llm', 'text', 'generation', 'writing'],
  nodeComponent: 'ai',

  inputs: [
    { id: 'input_topic', label: 'Topic', type: 'string', optional: false, position: 'left' },
    { id: 'input_style', label: 'Style', type: 'string', optional: true, position: 'left' },
    { id: 'input_constraints', label: 'Constraints', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_text', label: 'Generated Text', type: 'string', description: 'Generated text content', position: 'right' },
    { id: 'output_metadata', label: 'Metadata', type: 'json', description: 'Generation metadata (tokens, latency, etc.)', position: 'right' },
  ],

  configFields: [
    {
      id: 'model',
      label: 'Model',
      type: 'dropdown',
      default: 'gpt-4-turbo',
      options: [
        { label: 'GPT-4 Turbo', value: 'gpt-4-turbo' },
        { label: 'GPT-3.5 Turbo', value: 'gpt-3.5-turbo' },
        { label: 'Claude 3', value: 'claude-3' },
        { label: 'Claude 3.5 Sonnet', value: 'claude-3.5-sonnet' },
      ],
      required: true,
      description: 'LLM model to use for text generation',
    },
    {
      id: 'systemPrompt',
      label: 'System Prompt',
      type: 'code',
      default: 'You are a helpful assistant that generates high-quality text.',
      required: true,
      description: 'System prompt that guides the AI behavior',
    },
    {
      id: 'temperature',
      label: 'Temperature',
      type: 'slider',
      default: 0.7,
      min: 0,
      max: 2,
      description: 'Controls randomness (0 = deterministic, 2 = creative)',
    },
    {
      id: 'llmService',
      label: 'LLM Service',
      type: 'dropdown',
      default: 'azureOpenAI',
      options: [
        { label: 'Azure OpenAI', value: 'azureOpenAI' },
        { label: 'OpenAI', value: 'openAI' },
        { label: 'Anthropic', value: 'anthropic' },
      ],
      description: 'LLM provider service',
    },
    {
      id: 'maxTokens',
      label: 'Max Tokens',
      type: 'number',
      default: 2000,
      min: 1,
      max: 4096,
      description: 'Maximum tokens in the response',
    },
    {
      id: 'topP',
      label: 'Top P',
      type: 'slider',
      default: 1.0,
      min: 0,
      max: 1,
      description: 'Nucleus sampling parameter',
    },
    {
      id: 'stopSequences',
      label: 'Stop Sequences',
      type: 'textarea',
      default: '',
      description: 'Sequences where the API will stop generating further tokens (one per line)',
    },
  ],

  validation: [
    {
      id: 'model_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.model,
        reason: 'Model is required',
      }),
    },
    {
      id: 'systemPrompt_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.systemPrompt,
        reason: 'System prompt is required',
      }),
    },
    {
      id: 'temperature_range',
      check: (data: NodeData) => ({
        isValid:
          typeof data.configuration?.temperature === 'number' &&
          (data.configuration.temperature as number) >= 0 &&
          (data.configuration.temperature as number) <= 2,
        reason: 'Temperature must be between 0 and 2',
      }),
    },
  ],
};
