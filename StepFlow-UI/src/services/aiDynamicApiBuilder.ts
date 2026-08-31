// ============================================================================
// AI Dynamic API Builder — generates one dynamic-API operation at a time from
// a REST method + a one-line intent, grounded in real workspace data (attribute
// domains + sample EAV rows, flows, data-exchange profiles). Uses the saved
// local model config via callAiApi; pure helpers are exported for unit testing.
// Validation mirrors DynamicApisController.Save so drafts pass server rules.
// ============================================================================

import { callAiApi } from '@stores/useAiAssistantStore';
import type { AiModelConfig } from '@stores/useAiModelConfigStore';
import { DynamicApiService, type HandlerType } from '@services/dynamicApiService';

const SYSTEM_PROMPT = `You are a dynamic API operation builder for the StepFlow platform. You output ONE JSON object and nothing else — no markdown fences, no commentary before or after.

## Operation shape (camelCase keys exactly as shown)
{
  "method": string,        // GET | POST | PUT | PATCH | DELETE
  "path": string,          // "" for the api base path, or "/..." relative to it; "{name}" template segments allowed
  "handlerType": string,   // flow | attributeDomain | eav | dataExchange
  "flowId"?: string,       // required when handlerType is 'flow' — an existing flow id from the context
  "domainName"?: string,   // for 'attributeDomain'/'eav': a domain name from the context (omit to use the api-level attributeDomain)
  "profileId"?: string,    // required when handlerType is 'dataExchange' — an existing profile id/name from the context
  "description"?: string   // one or two sentences; for eav GET collection ops describe intended filters/sort/projection using the query language below
}

## Validation rules (your draft MUST satisfy every one of these)
1. method must be one of GET, POST, PUT, PATCH, DELETE.
2. path must be empty or start with '/'.
3. The full path is basePath + path normalized (duplicate slashes collapsed, single leading '/', no trailing '/' except bare "/"). It must not equal "/apis" and must not start with "/apis/" — that prefix is reserved for API management.
4. Every "{param}" template segment in the full path must match ^[A-Za-z_][A-Za-z0-9_]*$ (letters/digits/underscore, starting with a letter or underscore).
5. handlerType must be one of flow, attributeDomain, eav, dataExchange.
6. handlerType 'flow' requires flowId; 'dataExchange' requires profileId; 'attributeDomain' and 'eav' need a domain (domainName, or the api-level attributeDomain when domainName is omitted).
7. An eav GET operation supports at most one {param} in its full path.
8. The draft must not duplicate an existing route of this API (same method + same normalized full path) — they are listed under "Existing routes" in the user message.

## EAV GET semantics (handlerType 'eav', method GET) — pick exactly one shape:
- ""                     -> Collection: all rows; the query language applies verbatim.
- "/{id}"                -> Lookup: single row by entityId (falls back to rowKeyId); only 'fields' is honored in the query string.
- "/{id}/<secondSegment>" -> EntityFilter: collection filtered to entityId = {id} (e.g. "/{id}/comments").

## EAV non-GET semantics (handlerType 'eav'):
- POST  ""            -> append a row; body is the row values (top-level entityId/entityType become row fields).
- PUT/PATCH "/{rowKeyId}" -> replace/merge one row by its rowKeyId path parameter + JSON body.
- DELETE "/{rowKeyId}"    -> remove one row by its rowKeyId path parameter.

## EAV query language (for Collection/EntityFilter ops — mention what you intend in description):
- any non-reserved key filters rows by field equality (top-level row fields or captured values keys); multiple values for one key OR, different keys AND; 'entityId' is an ordinary filter key.
- sort=capturedAtUtc,-entityId : comma list, '-' prefix descends.
- pagination: limit (default 100), offset, or page (1-based).
- fields=a,b projects rows to those names (rowKeyId always kept).

## Handler selection guidance
- 'eav' for row CRUD on an attribute domain — the default choice when the intent is about reading/writing captured data.
- 'attributeDomain' for reading/updating the schema definition itself, not its rows.
- 'flow' when the logic belongs to a state machine — flowId MUST be an existing flow id from the context (never invent one).
- 'dataExchange' when an existing profile should run — profileId MUST come from the context (never invent one).

## Rules
1. Output raw JSON only — no code fences, no text before or after.
2. Use ONLY domain names, flow ids and profile ids that appear in the context; never invent identifiers.
3. Omit domainName when the api-level attributeDomain fits; set it only to override per operation.
4. Keep literal path segments short (lowercase-kebab); use {id} for identity parameters and {rowKeyId} where EAV non-GET semantics require it.`;

/** One attribute-domain entry of the AI context block (trimmed to what a small model needs). */
export interface AiDomainInfo {
  name: string;
  attributes: { name: string; dataType: number }[]; // dataType: 0 String | 1 Boolean | 2 Number | 3 Date | 4 Object | 5 Array
}

/** Compact workspace context injected into the user message before each generation call. */
export interface AiOpContext {
  domains: AiDomainInfo[];
  /** Up to a few sample EAV rows per domain (real field names/values ground the model). */
  sampleRows: Record<string, unknown[]>;
  flows: { id: string; name: string }[];
  profiles: { id: string; name: string }[];
}

/** One generated operation draft — camelCase wire shape of a dynamic-API op. */
export interface AiOpDraft {
  method: string;
  path: string;
  handlerType: HandlerType;
  flowId?: string | null;
  domainName?: string | null;
  profileId?: string | null;
  description?: string | null;
}

/** The API shell the new op will be added to (needed for route validation). */
export interface AiApiShell {
  basePath: string;
  attributeDomain?: string | null;
  existingOps: AiOpDraft[];
}

const ALLOWED_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
const HANDLER_TYPES: HandlerType[] = ['flow', 'attributeDomain', 'eav', 'dataExchange'];
// Mirrors DynamicApisController.TemplateParamRegex.
const TEMPLATE_PARAM_RE = /^[A-Za-z_][A-Za-z0-9_]*$/;

/** Extract the first JSON object from a model response (fences/prose tolerated). */
export function extractOpJson(raw: string): unknown {
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

/** Build the user message: intent + compact context block + routes to avoid. */
export function buildUserMessage(method: string, intent: string, ctx: AiOpContext, shell: AiApiShell): string {
  const existingRoutes = shell.existingOps.map((o) => `${o.method} ${DynamicApiService.joinPaths(shell.basePath, o.path)}`);

  const contextBlock = JSON.stringify({
    domains: ctx.domains.map((d) => ({ name: d.name, attributes: d.attributes })),
    sampleRows: ctx.sampleRows,
    flows: ctx.flows,
    profiles: ctx.profiles,
  });

  return [
    `Create a ${method} operation for this dynamic API.`,
    `Intent: ${intent.trim()}`,
    `API shell: basePath "${shell.basePath}", api-level attributeDomain ${shell.attributeDomain ? `"${shell.attributeDomain}"` : '(none)'}.`,
    existingRoutes.length > 0 ? `Existing routes (do not duplicate): ${existingRoutes.join(', ')}` : 'No existing operations yet.',
    'Context:',
    contextBlock,
  ].join('\n');
}

/**
 * Validate one op draft against the API shell + sibling ops. Mirrors the
 * payload-internal rules of DynamicApisController.Save (server-side checks that
 * need other APIs — workspace node existence, cross-API route conflicts — are
 * not covered and surface as server errors at save time). Returns human-readable
 * error strings; empty array = valid.
 */
export function validateOpDraft(draft: AiOpDraft, shell: AiApiShell): string[] {
  const errors: string[] = [];

  const method = (draft.method ?? '').trim().toUpperCase();
  if (!ALLOWED_METHODS.includes(method)) {
    errors.push(`method must be one of ${ALLOWED_METHODS.join(', ')}`);
  }

  const path = draft.path ?? '';
  if (path !== '' && !path.startsWith('/')) {
    errors.push('path must be empty or start with "/"');
  }

  const fullPath = DynamicApiService.joinPaths(shell.basePath, path);
  if (fullPath === '/apis' || fullPath.startsWith('/apis/')) {
    errors.push('the full path cannot use the reserved "/apis" management prefix');
  }

  for (const seg of fullPath.split('/').filter(Boolean)) {
    if (seg.length > 2 && seg.startsWith('{') && seg.endsWith('}')) {
      const param = seg.slice(1, -1);
      if (!TEMPLATE_PARAM_RE.test(param)) {
        errors.push(`template parameter "{${param}}" must match ^[A-Za-z_][A-Za-z0-9_]*$`);
      }
    }
  }

  const rawHandler = (draft.handlerType ?? '').trim().toLowerCase();
  const handlerType = HANDLER_TYPES.find((h) => h.toLowerCase() === rawHandler);
  if (!handlerType) {
    errors.push(`handlerType must be one of ${HANDLER_TYPES.join(', ')}`);
  } else {
    switch (handlerType) {
      case 'flow':
        if (!(draft.flowId ?? '').trim()) errors.push("flowId is required for handlerType 'flow'");
        break;
      case 'dataExchange':
        if (!(draft.profileId ?? '').trim()) errors.push("profileId is required for handlerType 'dataExchange'");
        break;
      case 'attributeDomain':
      case 'eav': {
        const domain = (draft.domainName ?? '').trim() || (shell.attributeDomain ?? '').trim();
        if (!domain) errors.push('needs a domain: set domainName or the api-level attributeDomain');
        break;
      }
    }

    if (handlerType === 'eav' && method === 'GET') {
      const templateCount = fullPath.split('/').filter((s) => s.length > 2 && s.startsWith('{') && s.endsWith('}')).length;
      if (templateCount > 1) errors.push('eav GET operations support at most one {param} in the full path');
    }
  }

  const routeKey = `${method} ${fullPath}`;
  for (const other of shell.existingOps) {
    const otherMethod = (other.method ?? '').trim().toUpperCase();
    if (otherMethod === method && DynamicApiService.joinPaths(shell.basePath, other.path ?? '') === fullPath) {
      errors.push(`route conflict: ${routeKey} is already defined in this API`);
      break;
    }
  }

  return errors;
}

/** Coerce an extracted JSON object into a draft with sane defaults (missing keys -> empty). */
export function toOpDraft(value: unknown): AiOpDraft {
  const obj = value as Record<string, unknown>;
  const str = (v: unknown): string | null => (typeof v === 'string' && v.trim() ? v.trim() : null);
  return {
    method: str(obj.method) ?? '',
    path: typeof obj.path === 'string' ? obj.path : '',
    handlerType: HANDLER_TYPES.find((h) => h.toLowerCase() === (str(obj.handlerType) ?? '').trim().toLowerCase()) ?? 'eav',
    flowId: str(obj.flowId),
    domainName: str(obj.domainName),
    profileId: str(obj.profileId),
    description: str(obj.description),
  };
}
/** Thrown when a model response cannot be used; `draft` carries the best-effort parse for manual prefill. */
export class AiDraftError extends Error {
  constructor(message: string, public readonly draft?: AiOpDraft) {
    super(message);
    this.name = 'AiDraftError';
  }
}
/**
 * Generate one operation via the saved AI model config. Extracts + validates;
 * throws with the first validation error when the draft is unusable (the caller
 * may retry once with `validationFeedback` carrying that error, then fall back
 * to manual editing).
 */
export async function suggestOperation(
  config: AiModelConfig,
  method: string,
  intent: string,
  ctx: AiOpContext,
  shell: AiApiShell,
  validationFeedback?: string,
): Promise<AiOpDraft> {
  let userMessage = buildUserMessage(method, intent, ctx, shell);
  if (validationFeedback) {
    userMessage += `\n\nYour previous draft failed validation with: ${validationFeedback}\nFix these problems and output a corrected JSON object.`;
  }
  const raw = await callAiApi(config, SYSTEM_PROMPT, userMessage);

  let draft: AiOpDraft;
  try {
    draft = toOpDraft(extractOpJson(raw));
  } catch (err) {
    throw new AiDraftError(err instanceof Error ? err.message : String(err));
  }

  // The user picked the method — trust it over a model that second-guessed.
  if (!draft.method) draft.method = method;

  const errors = validateOpDraft(draft, shell);
  if (errors.length > 0) {
    throw new AiDraftError(`AI draft failed validation: ${errors[0]}`, draft);
  }
  return draft;
}
