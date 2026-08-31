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
  isPublished?: boolean; // exposed on external dynamic API hosts (DynamicApiHost)
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
  // ── Context fetches for the AI wizard (same-origin metadata endpoints) ─────────

  /** Attribute domains with their attributes trimmed to name + dataType. */
  static async domains(): Promise<{ name: string; attributes: { name: string; dataType: number }[] }[]> {
    const res = await fetch('/api/attribute-domains');
    if (!res.ok) return fail(res);
    const entries = (await res.json()) as Array<{ attributeDomain?: { attributeDomainName?: string; attributes?: Array<{ attributeName?: string; dataType?: number }> } | null }>;
    return entries
      .map((e) => ({
        name: e.attributeDomain?.attributeDomainName ?? '',
        attributes: (e.attributeDomain?.attributes ?? [])
          .filter((a) => typeof a.attributeName === 'string' && a.attributeName.length > 0)
          .map((a) => ({ name: a.attributeName as string, dataType: typeof a.dataType === 'number' ? a.dataType : 0 })),
      }))
      .filter((d) => d.name);
  }

  /** Flow ids + names. */
  static async flows(): Promise<{ id: string; name: string }[]> {
    const res = await fetch('/api/flows');
    if (!res.ok) return fail(res);
    const list = (await res.json()) as Array<{ id?: string | number; name?: string }>;
    return list
      .map((f) => ({ id: String(f.id ?? ''), name: f.name || String(f.id ?? '') }))
      .filter((f) => f.id);
  }

  /** Data exchange profiles (same value convention as the panel editor: profile name, falling back to #id). */
  static async profiles(): Promise<{ id: string; name: string }[]> {
    const res = await fetch('/api/data-exchange/profiles');
    if (!res.ok) return fail(res);
    const list = (await res.json()) as Array<{ dataExchangeProfileId?: number | string; dataExchangeProfileName?: string }>;
    return list.map((p) => ({ id: p.dataExchangeProfileName ?? String(p.dataExchangeProfileId), name: p.dataExchangeProfileName ?? `#${p.dataExchangeProfileId}` }));
  }

  /** Up to `limit` sample EAV rows for a domain (capped at 50). */
  static async eavSampleRows(domain: string, limit = 3): Promise<Record<string, unknown>[]> {
    const res = await fetch(`/api/eav/${encodeURIComponent(domain)}/rows?limit=${Math.max(1, Math.min(50, limit))}`);
    if (!res.ok) return fail(res);
    const body = (await res.json()) as { rows?: Record<string, unknown>[] };
    return body.rows ?? [];
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
