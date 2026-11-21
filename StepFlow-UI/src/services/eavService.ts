// ═══════════════════════════════════════════════════════════
// EAV Service — browser client for the captured attribute-row API.
// Rows are flat key/value records stored per domain (eav-data/{domain}.json);
// form captures, flow outputs and eav://read all land here. The query language
// mirrors the engine's dynamic-API dispatcher: any non-reserved query key is an
// equality filter (repeated keys OR, different keys AND), sort=capturedAtUtc,-key,
// page/limit/offset pagination, fields=a,b projection.
// ═══════════════════════════════════════════════════════════

export interface EavDomain {
  domain: string;
  rows: number;
  hasRegistryContract: boolean;
  hasAttributeDomain: boolean;
}

/** A shaped row: rowKeyId + top-level fields (entityId, entityType, sourceTaskId, capturedAtUtc) + captured values. */
export interface EavRowRecord {
  rowKeyId: string;
  [key: string]: unknown;
}

export interface EavRowsPage {
  rows: EavRowRecord[];
  count: number;
}

export interface EavListOptions {
  /** key=value pairs, e.g. { entityId: 'e1', status: 'open' } — equality filters (AND across keys). */
  filter?: Record<string, string>;
  sort?: string;
  page?: number;
  limit?: number;
  offset?: number;
  fields?: string;
}

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body - keep the status message */ }
  throw new Error(message);
}

function domainPath(domain: string): string {
  return encodeURIComponent(domain);
}

export const EavService = {
  /** All domains with row counts and contract flags (registry entity / attribute domain). */
  async listDomains(): Promise<EavDomain[]> {
    const res = await fetch('/api/eav/domains');
    if (!res.ok) return fail(res);
    return (await res.json()) as EavDomain[];
  },

  /** One page of rows for a domain with the full query language. */
  async listRows(domain: string, opts?: EavListOptions): Promise<EavRowsPage> {
    const params = new URLSearchParams();
    if (opts?.filter) {
      for (const [key, value] of Object.entries(opts.filter)) params.append(key, value);
    }
    if (opts?.sort) params.set('sort', opts.sort);
    if (opts?.page !== undefined) params.set('page', String(opts.page));
    if (opts?.limit !== undefined) params.set('limit', String(opts.limit));
    if (opts?.offset !== undefined) params.set('offset', String(opts.offset));
    if (opts?.fields) params.set('fields', opts.fields);
    const qs = params.toString();
    const res = await fetch(`/api/eav/${domainPath(domain)}/rows${qs ? `?${qs}` : ''}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as EavRowsPage;
  },

  /** Append a row. Top-level entityId/entityType become row fields; the rest are captured values. */
  async createRow(domain: string, body: Record<string, unknown>): Promise<EavRowRecord> {
    const res = await fetch(`/api/eav/${domainPath(domain)}/rows`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as EavRowRecord;
  },

  /** Replace a row's values wholesale by row key. */
  async updateRow(domain: string, rowKeyId: string, values: Record<string, unknown>): Promise<EavRowRecord> {
    const res = await fetch(`/api/eav/${domainPath(domain)}/rows/${encodeURIComponent(rowKeyId)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as EavRowRecord;
  },

  /** Merge a partial patch into an existing row's values. */
  async patchRow(domain: string, rowKeyId: string, values: Record<string, unknown>): Promise<EavRowRecord> {
    const res = await fetch(`/api/eav/${domainPath(domain)}/rows/${encodeURIComponent(rowKeyId)}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as EavRowRecord;
  },

  /** Remove a row by key. */
  async deleteRow(domain: string, rowKeyId: string): Promise<{ status: string; rowKeyId: string }> {
    const res = await fetch(`/api/eav/${domainPath(domain)}/rows/${encodeURIComponent(rowKeyId)}`, { method: 'DELETE' });
    if (!res.ok) return fail(res);
    return (await res.json()) as { status: string; rowKeyId: string };
  },
};
