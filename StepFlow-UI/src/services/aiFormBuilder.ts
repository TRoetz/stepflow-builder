// ============================================================================
// AI Form Builder - generates a form definition, an attribute domain contract,
// and a capture/save flow from a natural-language description using the saved
// local model config (LM Studio / Ollama / llama.cpp / OpenAI-compatible).
// Pure helpers are exported for unit testing; the network call goes through
// callAiApi from the assistant store.
// ============================================================================

import { callAiApi } from '@stores/useAiAssistantStore';
import { useAiModelConfigStore, type AiModelConfig } from '@stores/useAiModelConfigStore';
import type { AttributeDomainEntry, FormDefinition } from '@services/formService';
import type { StateMachineDefinition } from '@services/flowService';

/** The form part of an AI-generated plan (draft shape for the forms editor). */
export interface AiFormDraft extends Pick<FormDefinition, 'formId' | 'title'> {
  description?: string | null;
  attributeDomainName?: string | null;
  /** Full UIPage JSON with PascalCase keys. */
  page: Record<string, unknown>;
}

/** A complete AI-generated plan: form + optional domain contract + optional capture/save flow. */
export interface AiFormPlan {
  form: AiFormDraft;
  domain?: AttributeDomainEntry | null;
  flow?: StateMachineDefinition | null;
}

export interface AiFormBuildInput {
  description: string;
  sampleData?: string;
}

const SYSTEM_PROMPT = `You are a form and flow designer for the StepFlow platform. You output ONE JSON document and nothing else — no markdown fences, no commentary before or after.

## Document shape
{
  "form": { ... },          // required
  "domain": { ... } | null, // attribute contract; null only when the user explicitly does not want validation/persistence binding
  "flow": { ... } | null    // ASL flow that captures via the form and saves downstream
}

## form (required) — camelCase keys
{
  "formId": "kebab-case-id",                    // required, stable id referenced by flows
  "title": "Human Readable Title",              // required
  "description": "One sentence.",               // optional
  "attributeDomainName": "PascalCase" | null,   // must equal domain.attributeDomain.attributeDomainName when a domain is provided
  "page": { ... }                               // UIPage document (see below)
}

## page — UIPage with PascalCase keys exactly as shown
{
  "Id": 1,
  "Title": "<form title>",
  "Description": "...",
  "Author": "stepflow-builder",
  "PageName": 0,
  "PageVersion": "1.0",
  "RootElements": [ ...elements ]
}

### Element types (use only these)
- Section: { Id, Type:"Section", Label, Heading, Children:[...] } — group related fields.
- Grid: { Id, Type:"Grid", Columns:2, Children:[{Id,Type:"GridItem",Children:[...]}] }.
- TextBox / InputGroup: single-line input. Keys: Id, Type, Attribute (camelCase name), Label, Placeholder?, Required? (boolean).
- TextArea: multi-line; add Rows:3 and Cols:40.
- Dropdown: { Id, Type:"Dropdown", Attribute, Label, Children:[{Id,Type:"DropdownOption",Value,DisplayText}, ...] }.
- Checkbox: boolean field.
- RadioButton group: a parent { Id, Type:"RadioButton", Attribute, Label } whose Children are RadioButtons sharing the same GroupName; each child has its own Id and Label (the submitted value is the child's Label).
- DatePicker: date input.
- Slider: add MinValue and MaxValue numbers.
- FileUpload: file picker.
- StaticText / Button: display-only, no Attribute.

Every data-capturing element MUST set "Attribute" to a camelCase name; attribute names must be unique across the page. Element Ids are unique integers starting at 10 (the page Id is 1).

## domain — attribute contract for validation and EAV persistence
{
  "schemaDefinition": null,
  "attributeDomain": {
    "version": "1",
    "attributeDomainName": "PascalCase name matching form.attributeDomainName",
    "description": "...",
    "isCurrentVersion": true,
    "attributes": [
      {
        "attributeName": "camelCase — must exactly match a page Attribute value",
        "dataType": 0,            // 0 String | 1 Boolean | 2 Number | 3 Date | 4 Object | 5 Array
        "displayName": "...",     // required string (may be empty)
        "placeholder": "",        // required string (may be empty)
        "helpText": "",           // required string (may be empty)
        "visible": true,
        "readOnly": false,
        "primaryKey": false,      // at most one attribute may be the primary key; set it on a natural id field when present
        "validationSchemaJson": "{\\"required\\":true}"   // optional JSON string with required/minimum/maximum/minLength/maxLength/pattern
      }
    ]
  }
}
Provide one attribute per page Attribute value, in the same order.

## flow — ASL document in camelCase that captures via the form and saves downstream
{
  "startAt": "<first state name>",
  "states": { ... }
}
Rules:
- State names are PascalCase (e.g. CaptureOrder, SaveOrder, Done).
- The first state MUST be a FormCapture state:
  { "type":"FormCapture", "comment":"...", "task": { "formId": <form.formId>, "title": "...", "assignee": "<team or person>" }, "next": "<save state name>" }
  Omit resultPath unless there are multiple capture states (then use "$.captured<Name>").
- The save state MUST be a Task that persists the captured data:
  - EAV write (default when a domain is provided): { "type":"Task", "comment":"...", "resource": "eav://<DomainName>", "parameters": { "operation":"write", "entityType":"<DomainName>" }, "next":"<done state name>" }
  - HTTP POST (only when the user provides an endpoint URL): { "type":"Task", "comment":"...", "resource":"https://host/path", "parameters": { "method":"POST", "url":"https://host/path", "headers":"{\\"Content-Type\\":\\"application/json\\"}" }, "next":"<done state name>" }
- The final state MUST be a terminal: { "type":"Succeed" }.

## Rules
1. Output raw JSON only — no code fences, no text before or after.
2. Keep the form focused on what the user asked for; do not invent extra fields beyond reasonable defaults.
3. Use sample data (when provided) to infer field types and example placeholders.
4. dataType must match the field: free text 0, yes/no 1, numeric 2, date/time 3, nested object 4, list of items 5.`;

/** Extract the first JSON object from a model response (fences/prose tolerated). */
export function extractFormPlanJson(raw: string): unknown {
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

function isPlainObject(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

/** Validate and normalize an extracted plan. Throws with descriptive errors on contract violations. */
export function validateFormPlan(value: unknown): AiFormPlan {
  if (!isPlainObject(value)) throw new Error('Model response was not a JSON object.');

  const form = value.form;
  if (!isPlainObject(form)) throw new Error('Model response is missing the "form" section.');
  if (typeof form.formId !== 'string' || !form.formId.trim()) {
    throw new Error('Form plan is missing a non-empty formId — refine the description and try again.');
  }
  if (typeof form.title !== 'string' || !form.title.trim()) {
    throw new Error('Form plan is missing a title — refine the description and try again.');
  }
  const page = form.page;
  if (!isPlainObject(page)) throw new Error('Form plan is missing its "page" (UIPage) document.');
  if (!Array.isArray(page.RootElements) || page.RootElements.length === 0) {
    throw new Error('Form page has no RootElements — refine the description and try again.');
  }

  const domain = value.domain ?? null;
  if (domain !== null && !isPlainObject(domain)) {
    throw new Error('"domain" must be an object or null.');
  }
  if (domain) {
    const ad = isPlainObject(domain.attributeDomain) ? domain.attributeDomain : null;
    if (!ad || typeof ad.attributeDomainName !== 'string' || !ad.attributeDomainName.trim()) {
      throw new Error('Domain plan is missing attributeDomain.attributeDomainName.');
    }
    if (form.attributeDomainName && form.attributeDomainName !== ad.attributeDomainName) {
      throw new Error(`Form binds domain "${form.attributeDomainName}" but the plan defines "${ad.attributeDomainName}".`);
    }
    if (!Array.isArray(ad.attributes) || ad.attributes.length === 0) {
      throw new Error('Domain plan has no attributes.');
    }
    for (const attr of ad.attributes as Array<Record<string, unknown>>) {
      if (!isPlainObject(attr) || typeof attr.attributeName !== 'string' || !attr.attributeName.trim()) {
        throw new Error('Every domain attribute needs a non-empty attributeName.');
      }
      if (typeof attr.dataType !== 'number') {
        throw new Error(`Domain attribute "${attr.attributeName}" is missing its dataType ordinal.`);
      }
    }
  }

  const flow = value.flow ?? null;
  if (flow !== null && !isPlainObject(flow)) {
    throw new Error('"flow" must be an object or null.');
  }
  if (flow) {
    if (typeof flow.startAt !== 'string' || !flow.startAt.trim()) {
      throw new Error('Flow plan is missing startAt.');
    }
    const states = flow.states;
    if (!isPlainObject(states) || Object.keys(states).length === 0) {
      throw new Error('Flow plan has no states.');
    }
    const first = states[flow.startAt];
    if (!isPlainObject(first)) throw new Error(`Flow start state "${flow.startAt}" is not defined in states.`);
    if (first.type !== 'FormCapture') {
      throw new Error('The flow must start with a FormCapture state that presents the generated form.');
    }
    const task = first.task;
    if (!isPlainObject(task) || typeof task.formId !== 'string' || task.formId !== form.formId) {
      throw new Error(`Flow capture step must reference the generated form id "${form.formId}".`);
    }
  }

  return {
    form: {
      formId: form.formId.trim(),
      title: form.title,
      description: typeof form.description === 'string' ? form.description : null,
      attributeDomainName: domain && isPlainObject(domain.attributeDomain)
        ? (domain.attributeDomain as Record<string, unknown>).attributeDomainName as string
        : typeof form.attributeDomainName === 'string'
          ? form.attributeDomainName
          : null,
      page,
    },
    domain: domain as AttributeDomainEntry | null,
    flow: flow as StateMachineDefinition | null,
  };
}

/** Build a form + domain + flow via the saved AI model config. */
export async function buildFormWithAi(input: AiFormBuildInput): Promise<AiFormPlan> {
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
    maxTokens: Math.max(state.maxTokens, 4096), // full plan JSON needs headroom
    topP: state.topP,
  };

  const parts: string[] = [];
  parts.push(`Requirement: ${input.description.trim()}`);
  if (input.sampleData && input.sampleData.trim()) {
    parts.push(`Sample data:\n${input.sampleData.trim()}`);
  }

  const raw = await callAiApi(config, SYSTEM_PROMPT, parts.join('\n\n'));
  return validateFormPlan(extractFormPlanJson(raw));
}
