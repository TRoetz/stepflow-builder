// ============================================================================
// Dynamic API Service Client
// Talks to /api/dynamic/apis (definition CRUD) and /api/dynamic/openapi.json.
// Wire shape is camelCase, matching the C# models in DynamicApi/DynamicApiModels.cs.
// Node paths are "org/project/sub" (forward slashes), depth 1-3.
// ============================================================================

export type HandlerType = 'flow' | 'attributeDomain' | 'eav' | 'dataExchange';

/** One REST operation of a dynamic API (camelCase wire shape). */
export interface DynamicApiOperation {
  method: string; // GET | POST | PUT | PATCH | DELETE
  path: string;   // "" or "/..." relative to the api's basePath; "{name}" template segments allowed
  handlerType: HandlerType;
  flowId?: string | null;      // handler: flow
  domainName?: string | null;  // handlers: attributeDomain, eav (falls back to the api-level domain)
  profileId?: string | null;   // handler: dataExchange
  description?: string | null;
}

/** A user-defined REST API attached to a workspace node. */
export interface DynamicApiDefinition {
  id: string;
  name: string;
  description?: string | null;
  nodePath: string; // "org" | "org/project" | "org/project/sub"
  basePath: string; // starts with "/", e.g. "/orders" or "/"
  attributeDomain?: string | null;
  bearerToken?: string | null; // empty/null = open access
  isActive: boolean;
  operations: DynamicApiOperation[];
  createdAt?: string;
  updatedAt?: string;
}

/** Payload for POST /api/dynamic/apis (id omitted on create). */
export type DynamicApiSavePayload = Omit<DynamicApiDefinition, 'createdAt' | 'updatedAt' | 'id'> & { id?: string };

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body - keep the status message */ }
  throw new Error(message);
}

export class DynamicApiService {
  /** All APIs, optionally filtered to a node path (segment-aware prefix: "Acme" also matches "Acme/P/S"). */
  static async list(nodePath?: string): Promise<DynamicApiDefinition[]> {
    const res = await fetch(`/api/dynamic/apis${nodePath ? `?nodePath=${encodeURIComponent(nodePath)}` : ''}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as DynamicApiDefinition[];
  }

  /** A single API by id. */
  static async get(id: string): Promise<DynamicApiDefinition> {
    const res = await fetch(`/api/dynamic/apis/${encodeURIComponent(id)}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as DynamicApiDefinition;
  }

  /** Creates or replaces an API. Returns the stored id and whether it was new. */
  static async save(def: DynamicApiSavePayload): Promise<{ id: string; created: boolean }> {
    const res = await fetch('/api/dynamic/apis', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(def),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as { id: string; created: boolean };
  }

  /** Deletes an API by id. */
  static async remove(id: string): Promise<void> {
    const res = await fetch(`/api/dynamic/apis/${encodeURIComponent(id)}`, { method: 'DELETE' });
    if (!res.ok) return fail(res);
  }

  /** Generated OpenAPI 3 document covering all active APIs. */
  static async openApi(): Promise<Record<string, unknown>> {
    const res = await fetch('/api/dynamic/openapi.json');
    if (!res.ok) return fail(res);
    return (await res.json()) as Record<string, unknown>;
  }

  /** Mirrors DynamicApiMatcher.JoinPaths: collapse '//', single leading '/', no trailing '/' (except bare "/"). */
  static joinPaths(basePath: string, opPath: string): string {
    let p = (basePath || '/') + (opPath || '');
    p = p.replace(/\\/g, '/');
    while (p.includes('//')) p = p.replace('//', '/');
    if (!p.startsWith('/')) p = '/' + p;
    if (p.length > 1) p = p.replace(/\/+$/, '');
    return p;
  }

  /** Full request URL for an operation of a dynamic API. */
  static requestUrl(basePath: string, opPath: string): string {
    return `/api/dynamic${DynamicApiService.joinPaths(basePath, opPath)}`;
  }
}
