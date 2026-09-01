import { StepSchema, NodeData } from '@schema-types/schema';

// ── HTTP Request ──
export const httpRequestSchema: StepSchema = {
  schemaId: 'stepflow:api:http',
  name: 'HTTP Request',
  category: 'api',
  description: 'Make HTTP requests to external APIs and services.',
  icon: 'send',
  color: '#10B981',
  version: '1.0.0',
  isTemplate: true,
  tags: ['http', 'rest', 'api', 'request', 'web'],
  nodeComponent: 'api',

  inputs: [
    { id: 'input_body', label: 'Request Body', type: 'json', optional: true, position: 'left' },
    { id: 'input_headers', label: 'Headers', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_response', label: 'Response', type: 'json', description: 'API response body', position: 'right' },
    { id: 'output_status', label: 'Status Code', type: 'number', description: 'HTTP status code', position: 'right' },
  ],

  configFields: [
    {
      id: 'method',
      label: 'Method',
      type: 'dropdown',
      default: 'GET',
      options: [
        { label: 'GET', value: 'GET' },
        { label: 'POST', value: 'POST' },
        { label: 'PUT', value: 'PUT' },
        { label: 'PATCH', value: 'PATCH' },
        { label: 'DELETE', value: 'DELETE' },
        { label: 'HEAD', value: 'HEAD' },
      ],
      required: true,
      description: 'HTTP method',
    },
    {
      id: 'url',
      label: 'URL',
      type: 'text',
      required: true,
      description: 'Target URL for the request',
    },
    {
      id: 'body',
      label: 'Body (JSON)',
      type: 'json',
      default: '{}',
      condition: (data: NodeData) => {
        const m = String(data.configuration?.method ?? 'GET').toUpperCase();
        return m === 'POST' || m === 'PUT' || m === 'PATCH';
      },
      description: 'Request body as JSON (sent with POST/PUT/PATCH)',
    },
    {
      id: 'headers',
      label: 'Headers',
      type: 'json',
      default: JSON.stringify({ 'Content-Type': 'application/json' }, null, 2),
      description: 'Request headers as JSON object',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 30000,
      min: 1000,
      max: 120000,
      description: 'Request timeout',
    },
    {
      id: 'includeStatus',
      label: 'Include Status Envelope',
      type: 'toggle',
      default: false,
      description: 'Wrap the response as {status, ok, body} and do not fail on non-2xx — for health checks that branch on the status code.',
    },
    {
      id: 'retryCount',
      label: 'Retry Count',
      type: 'number',
      default: 0,
      min: 0,
      max: 5,
      description: 'Number of retry attempts on failure',
    },
    {
      id: 'retryDelay',
      label: 'Retry Delay (ms)',
      type: 'number',
      default: 1000,
      min: 100,
      max: 30000,
      condition: (data: NodeData) => (data.configuration?.retryCount as number ?? 0) > 0,
      description: 'Delay between retries',
    },
    {
      id: 'authentication',
      label: 'Authentication',
      type: 'dropdown',
      default: 'none',
      options: [
        { label: 'None', value: 'none' },
        { label: 'Bearer Token', value: 'bearer' },
        { label: 'Basic Auth', value: 'basic' },
        { label: 'API Key', value: 'apiKey' },
        { label: 'OAuth2', value: 'oauth2' },
      ],
      description: 'Authentication method',
    },
    {
      id: 'authToken',
      label: 'Auth Token',
      type: 'text',
      default: '',
      condition: (data: NodeData) => data.configuration?.authentication === 'bearer',
      description: 'Bearer token for authentication',
    },
  ],

  validation: [
    {
      id: 'url_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.url,
        reason: 'URL is required',
      }),
    },
    {
      id: 'url_valid',
      check: (data: NodeData) => {
        try {
          new URL(data.configuration?.url as string);
          return { isValid: true };
        } catch {
          return {
            isValid: false,
            reason: 'URL must be a valid URL',
          };
        }
      },
    },
  ],
};

// ── Registered API Call ──
export const registeredApiSchema: StepSchema = {
  schemaId: 'stepflow:api:registered',
  name: 'Registered API',
  category: 'api',
  description: 'Call a pre-registered API from the API registry.',
  icon: 'plug',
  color: '#10B981',
  version: '1.0.0',
  isTemplate: true,
  tags: ['api', 'registered', 'registry', 'integration'],
  nodeComponent: 'api',

  inputs: [
    { id: 'input_params', label: 'Parameters', type: 'json', optional: true, position: 'left' },
    { id: 'input_auth', label: 'Auth Context', type: 'json', optional: true, position: 'left' },
  ],

  outputs: [
    { id: 'output_response', label: 'Response', type: 'json', description: 'API response', position: 'right' },
    { id: 'output_errors', label: 'Errors', type: 'array', description: 'Any errors encountered', position: 'right' },
  ],

  configFields: [
    {
      id: 'apiId',
      label: 'API ID',
      type: 'api-selector',
      required: true,
      description: 'Select a registered API from the registry',
    },
    {
      id: 'endpoint',
      label: 'Endpoint',
      type: 'dropdown',
      default: '',
      options: [], // Populated dynamically from selected API
      required: true,
      description: 'API endpoint to call',
    },
    {
      id: 'timeout',
      label: 'Timeout (ms)',
      type: 'number',
      default: 30000,
      min: 1000,
      max: 120000,
      description: 'Request timeout',
    },
    {
      id: 'retryCount',
      label: 'Retry Count',
      type: 'number',
      default: 0,
      min: 0,
      max: 5,
      description: 'Number of retry attempts on failure',
    },
    {
      id: 'enableCaching',
      label: 'Enable Caching',
      type: 'toggle',
      default: false,
      description: 'Cache API responses',
    },
    {
      id: 'cacheTTL',
      label: 'Cache TTL (seconds)',
      type: 'number',
      default: 300,
      min: 1,
      max: 3600,
      condition: (data: NodeData) => data.configuration?.enableCaching === true,
      description: 'Time-to-live for cached responses',
    },
  ],

  validation: [
    {
      id: 'apiId_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.apiId,
        reason: 'API ID is required',
      }),
    },
    {
      id: 'endpoint_required',
      check: (data: NodeData) => ({
        isValid: !!data.configuration?.endpoint,
        reason: 'Endpoint is required',
      }),
    },
  ],
};
