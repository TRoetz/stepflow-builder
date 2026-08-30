// ============================================================================
// AI Profile Builder — generates/refines Data Exchange profile JSON using the
// saved local model config (LM Studio / Ollama / llama.cpp / OpenAI-compatible).
// Pure helpers are exported for unit testing; the network call goes through
// callAiApi from the assistant store.
// ============================================================================

import { callAiApi } from '@stores/useAiAssistantStore';
import { useAiModelConfigStore, type AiModelConfig } from '@stores/useAiModelConfigStore';
import type { DataExchangeProfile } from '@services/dataExchangeService';

const SYSTEM_PROMPT = `You are a Data Exchange profile builder for the StepFlow platform. You output ONE JSON document and nothing else — no markdown fences, no commentary before or after.

## Document shape (camelCase keys exactly as shown)
{
  "dataExchangeProfileName": string (required),
  "profileId": string | null,
  "flowId": string | null,
  "isActive": boolean,
  "dataSource": {
    "dataSourceName": string,
    "mediumType": number,            // 0 Api | 1 ApiOAuth | 2 Database | 3 File
    "mediumConfigurationJson": string, // JSON-encoded STRING (escaped), not a nested object. mediumType 3: {"filePath":"..."}; mediumType 2: {"connectionString":"...","query":"SELECT ..."}
    "importSchemaId": number,
    "importSchema": AttributeDomain   // the inbound (external) schema
  },
  "pipeline": {
    "pipelineStages": [               // ordered by executionOrder (start at 1)
      {
        "stageType": number,          // 0 DataTreatment | 1 PreRouting | 2 Routing | 3 PostRouting
        "executionOrder": number,
        "pipelineStageActions": [
          { "executionOrder": number, "action": Action }
        ]
      }
    ]
  }
}

## AttributeDomain (used for importSchema / inputSchema / actionSchema)
{ "attributeDomainName": string, "version": "1", "isCurrentVersion": true,
  "attributes": [ EntityAttribute ] }

## EntityAttribute
{ "entityAttributeId": number (unique within its domain, start at 10),
  "attributeName": string,
  "dataType": number /* 0 String | 1 Boolean | 2 Number | 3 Date | 4 Object | 5 Array */,
  "displayName"?: string, "description"?: string, "primaryKey"?: boolean, "visible"?: boolean }

## Action (by type)
- Transformation: { "actionName": string, "type": 1, "schemaMap": SchemaMap, "inputSchema"?: AttributeDomain | null, "actionSchema"?: AttributeDomain | null /* output schema */ }
- EnrichmentLookup: { "actionName": string, "type": 2, "lookupId"?: number, "endpoint"?: { "actionEndpointURL": string }, "parameters"?: object }
- Dispatch: { "actionName": string, "type": 3, "endpoint": { "actionEndpointURL": string /* e.g. file://out/orders.csv or https://... */ }, "parameters"?: object /* e.g. {"OutputFormat":"csv"} */ }

## SchemaMap (for Transformation actions)
{ "schemaMapName": string, "sourceSchemaId"?: number, "targetSchemaId"?: number,
  "attributeMappings": [
    {
      "targetAttribute": { "attributeName": string, "dataType": number },  // embedded target — preferred over ids
      "transformType": number /* 0 DirectCopy | 1 Combine (param "separator") | 2 Multiply | 3 SetDefault (param "defaultValue") | 4 Trim | 5 ToUpper | 6 FormatDate (param "format", e.g. "yyyy-MM-dd") */,
      "mergeStrategy"?: number /* 0 AddNewOnly | 1 OverwriteExisting | 2 AppendValue */,
      "sourceAttributes": [ { "attributeName": string } ],                 // fields from the inbound row, exact names
      "parameters"?: { key: string }                                       // only when the transform needs one
    }
  ] }

## Rules
1. Output raw JSON only — no code fences, no text before or after.
2. Every attribute in importSchema.attributes has a unique non-zero entityAttributeId (start at 10).
3. Derive importSchema attributes from the sample data when provided: one per column/key, with the correct dataType and sensible names.
4. Transformation mappings must reference inbound field names exactly as they appear in the sample/header.
5. Use stageType 0 (DataTreatment) for mapping/enrichment work; later stages only when routing is required.
6. When modifying an existing profile: preserve every part not contradicted by the requirement — same ids, names, and structure where possible.
7. mediumConfigurationJson must be a JSON-encoded STRING (escaped), never a nested object.`;

export interface AiBuildInput {
  description: string;
  sampleData?: string;
  baseProfile?: DataExchangeProfile | null;
}

/** Extract the first JSON object from a model response (fences/prose tolerated). */
export function extractProfileJson(raw: string): unknown {
  const text = raw.trim();
  if (!text) throw new Error('Model returned an empty response.');

  let candidate = '';
  const fence = /```(?:json)?\s*([\s\S]*?)```/i.exec(text);
  if (fence && fence[1]) {
    candidate = fence[1].trim();
  } else {
    const start = text.indexOf('{');
    const end = text.lastIndexOf('}');
    if (start === -1 || end <= start) throw new Error('Model response contained no JSON object.');
    candidate = text.slice(start, end + 1);
  }

  let value: unknown;
  try {
    value = JSON.parse(candidate);
  } catch (err) {
    throw new Error(`Invalid JSON in model response: ${err instanceof Error ? err.message : String(err)}`);
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('Model response was not a JSON object.');
  }
  return value;
}

/** Build or refine a profile via the saved AI model config. */
export async function buildProfileWithAi(input: AiBuildInput): Promise<DataExchangeProfile> {
  const state = useAiModelConfigStore.getState();
  if (!state.baseUrl || !state.defaultModel) {
    throw new Error('No AI model configured. Open Settings → AI Model and save a local endpoint (e.g. LM Studio).');
  }

  const config: AiModelConfig = {
    provider: state.provider,
    baseUrl: state.baseUrl,
    apiKey: state.apiKey,
    defaultModel: state.defaultModel,
    temperature: Math.min(state.temperature, 0.3), // deterministic structure
    maxTokens: Math.max(state.maxTokens, 4096), // full profile JSON needs headroom
    topP: state.topP,
  };

  const parts: string[] = [];
  parts.push(`Requirement: ${input.description.trim()}`);
  if (input.sampleData && input.sampleData.trim()) {
    parts.push(`Sample data:\n${input.sampleData.trim()}`);
  }
  if (input.baseProfile) {
    parts.push(
      'Existing profile — modify it, preserving everything not contradicted by the requirement:\n' +
        JSON.stringify(input.baseProfile)
    );
  } else {
    parts.push('Create a new profile from scratch.');
  }

  const raw = await callAiApi(config, SYSTEM_PROMPT, parts.join('\n\n'));
  const value = extractProfileJson(raw);
  if (typeof (value as Record<string, unknown>).dataExchangeProfileName !== 'string') {
    throw new Error('Model response is missing dataExchangeProfileName — refine the description and try again.');
  }
  return value as DataExchangeProfile;
}
