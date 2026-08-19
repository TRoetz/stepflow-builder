// ═══════════════════════════════════════════════════════════
// Flow Templates — starter flows the user can instantiate from
// the palette. Each template provides a main flow (AiFlowJson
// format: label-based nodes + edges, consumed by importFlow) and
// optionally an iterator body for its Map node's linked flow.
// ═══════════════════════════════════════════════════════════

export interface TemplateNode {
  schemaId: string;
  label: string;
  config?: Record<string, unknown>;
  /** Optional explicit canvas position — imported verbatim by FlowService.importFlow. */
  position?: { x: number; y: number };
}

/** Edge between nodes by their template labels (resolved on import). */
export interface TemplateEdge {
  source: string;
  target: string;
}

export interface TemplateFlow {
  nodes: TemplateNode[];
  edges: TemplateEdge[];
}

export interface FlowTemplate {
  id: string;
  name: string;
  icon: string;
  description: string;
  tags: string[];
  mainFlow: TemplateFlow;
  /** Optional body for the Map node's linked iterator flow. */
  iteratorBody?: TemplateFlow;
}

export const flowTemplates: FlowTemplate[] = [
  // ── Canonical EAV row-processing reference flow (Task 6) ──
  // SQL Query → Map (body = EAV row read → AI Text Gen → AI Decision)
  //            → Rule Engine → Script file write.
  {
    id: 'tpl-eav-row-processing',
    name: 'EAV Row Processing',
    icon: '📄',
    description:
      'EAV-driven row processing: SQL Query → Map (loop body = EAV row read → AI Text Gen → AI Decision) → Rule Engine → Script file write. The iterator body is created as a saved sub-flow and linked to the Map automatically.',
    tags: ['eav', 'sql', 'map', 'iterator', 'ai', 'rules'],
    mainFlow: {
      nodes: [
        { schemaId: 'stepflow:terminal:start', label: 'Start', position: { x: 0, y: 180 } },
        {
          schemaId: 'stepflow:data:sql',
          label: 'Query Rows',
          config: {
            connectionString: '<your-connection-string>',
            query: 'SELECT * FROM your_table',
          },
          position: { x: 280, y: 180 },
        },
        {
          schemaId: 'stepflow:flow:map',
          label: 'Process Each Row',
          config: { itemsPath: '$.rows' },
          position: { x: 560, y: 180 },
        },
        {
          schemaId: 'stepflow:rule:rule_engine',
          label: 'Classify Result',
          config: {
            ruleSet: JSON.stringify(
              [
                { name: 'approved', expression: '$.score >= 90 || $.decision == "ok"', outcome: 'approved' },
                { name: 'fallback', expression: 'true', outcome: 'manual_review' },
              ],
              null,
              2
            ),
          },
          position: { x: 840, y: 180 },
        },
        {
          schemaId: 'stepflow:transform:script',
          label: 'Write Output File',
          config: {
            language: 'javascript',
            script:
              '// Write the classified rows to an output file (placeholder — adjust path/format).\nconst fs = require("fs");\nfs.writeFileSync("output_rows.json", JSON.stringify(context.input.rows ?? context.input, null, 2));\nreturn context.input;',
          },
          position: { x: 1120, y: 180 },
        },
        { schemaId: 'stepflow:terminal:end', label: 'End', position: { x: 1400, y: 180 } },
      ],
      edges: [
        { source: 'Start', target: 'Query Rows' },
        { source: 'Query Rows', target: 'Process Each Row' },
        { source: 'Process Each Row', target: 'Classify Result' },
        { source: 'Classify Result', target: 'Write Output File' },
        { source: 'Write Output File', target: 'End' },
      ],
    },
    // Loop body executed once per item — the reference chain from Task 6.
    iteratorBody: {
      nodes: [
        { schemaId: 'stepflow:utility:pass', label: 'Iteration Start', position: { x: 0, y: 180 } },
        {
          schemaId: 'stepflow:data:eav',
          label: 'Read Row',
          config: { operation: 'read', entityType: '<your-entity-type>' },
          position: { x: 280, y: 180 },
        },
        {
          schemaId: 'stepflow:ai:text',
          label: 'Generate Text',
          config: {
            systemPrompt:
              'Summarize the current source row into a concise text description. Use only fields available on $input.row.',
          },
          position: { x: 560, y: 180 },
        },
        {
          schemaId: 'stepflow:ai:decision',
          label: 'Decide',
          config: {
            prompt:
              'Evaluate the generated summary for the current row and decide whether it is approved or needs manual review. Return a single-word decision.',
          },
          position: { x: 840, y: 180 },
        },
      ],
      edges: [
        { source: 'Iteration Start', target: 'Read Row' },
        { source: 'Read Row', target: 'Generate Text' },
        { source: 'Generate Text', target: 'Decide' },
      ],
    },
  },
  {
    id: 'tpl-refund-review',
    name: 'AI Refund Review',
    icon: '🤖',
    description:
      'Fetch refund requests, normalize them, then review each row in an iterator sub-flow with an AI decision.',
    tags: ['ai', 'map', 'iterator', 'eav'],
    mainFlow: {
      nodes: [
        { schemaId: 'stepflow:terminal:start', label: 'Start' },
        {
          schemaId: 'stepflow:data:eav',
          label: 'Fetch Requests',
          config: { operation: 'read', entityType: 'customer_refund_request' },
        },
        {
          schemaId: 'stepflow:transform:jsonata',
          label: 'Normalize Rows',
          config: { expression: '{ amount, reason, customer_id }' },
        },
        {
          schemaId: 'stepflow:flow:map',
          label: 'Review Each Row',
          config: {},
        },
        {
          schemaId: 'stepflow:data:eav',
          label: 'Store Decisions',
          config: { operation: 'write', entityType: 'refund_decision' },
        },
        { schemaId: 'stepflow:terminal:end', label: 'End' },
      ],
      edges: [
        { source: 'Start', target: 'Fetch Requests' },
        { source: 'Fetch Requests', target: 'Normalize Rows' },
        { source: 'Normalize Rows', target: 'Review Each Row' },
        { source: 'Review Each Row', target: 'Store Decisions' },
        { source: 'Store Decisions', target: 'End' },
      ],
    },
    iteratorBody: {
      nodes: [
        { schemaId: 'stepflow:utility:pass', label: 'Iteration Start' },
        {
          schemaId: 'stepflow:data:eav',
          label: 'Read Row',
          config: { operation: 'read', entityType: 'customer_refund_request' },
        },
        {
          schemaId: 'stepflow:ai:decision',
          label: 'Approve Refund?',
          config: {
            model: 'gpt-4-turbo',
            prompt:
              'Decide whether this refund request should be approved. Consider the amount and reason. Respond with a clear approve/deny decision.',
          },
        },
        { schemaId: 'stepflow:terminal:end', label: 'Iteration End' },
      ],
      edges: [
        { source: 'Iteration Start', target: 'Read Row' },
        { source: 'Read Row', target: 'Approve Refund?' },
        { source: 'Approve Refund?', target: 'Iteration End' },
      ],
    },
  },
  {
    id: 'tpl-api-health-check',
    name: 'API Health Check',
    icon: '🩺',
    description:
      'Ping an HTTP endpoint, branch on the result, and produce a success or failure report.',
    tags: ['api', 'http', 'choice', 'monitoring'],
    mainFlow: {
      nodes: [
        { schemaId: 'stepflow:terminal:start', label: 'Start' },
        {
          schemaId: 'stepflow:api:http',
          label: 'Check Endpoint',
          config: { method: 'GET', url: 'https://example.com/health', timeout: 5000 },
        },
        {
          schemaId: 'stepflow:flow:choice',
          label: 'Healthy?',
          config: { condition: '$.status < 400' },
        },
        {
          schemaId: 'stepflow:transform:jsonata',
          label: 'Report OK',
          config: { expression: '{ healthy: true, status: $.status }' },
        },
        {
          schemaId: 'stepflow:transform:jsonata',
          label: 'Report Issue',
          config: { expression: '{ healthy: false, status: $.status }' },
        },
        { schemaId: 'stepflow:terminal:end', label: 'End' },
      ],
      edges: [
        { source: 'Start', target: 'Check Endpoint' },
        { source: 'Check Endpoint', target: 'Healthy?' },
        { source: 'Healthy?', target: 'Report OK' },
        { source: 'Healthy?', target: 'Report Issue' },
        { source: 'Report OK', target: 'End' },
        { source: 'Report Issue', target: 'End' },
      ],
    },
  },
];
