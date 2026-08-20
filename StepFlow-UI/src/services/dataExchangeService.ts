// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DATA EXCHANGE SERVICE
// Thin fetch client for the /api/data-exchange/* endpoints on StepFunctionsApp.
// Profiles are free-form JSON documents (see DataExchangeProfileStore.cs);
// this service treats them as opaque objects and lets the panel edit raw JSON.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

const BASE = '/api/data-exchange';

export interface DataExchangeProfile {
  id?: string;
  name: string;
  description?: string;
  dataSourceId?: string | null;
  pipeline?: unknown[];
  [key: string]: unknown;
}

export interface ExecutionRecord {
  executionId: string;
  profileName: string;
  success: boolean;
  startedAt: number; // unix ms
  durationMs: number;
  error?: string | null;
  sourceFile?: string | null;
  result?: unknown;
}

async function parse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      if (body?.error) message = String(body.error);
    } catch {
      /* non-JSON error body */
    }
    throw new Error(message);
  }
  return res.json() as Promise<T>;
}

export const DataExchangeService = {
  // ── Profiles ────────────────────────────────────────────────────────────────
  async listProfiles(): Promise<DataExchangeProfile[]> {
    const res = await fetch(`${BASE}/profiles`);
    return parse<DataExchangeProfile[]>(res);
  },

  async getProfile(id: string): Promise<DataExchangeProfile> {
    const res = await fetch(`${BASE}/profiles/${encodeURIComponent(id)}`);
    return parse<DataExchangeProfile>(res);
  },

  /** POST /profiles — create (no id) or update (id present). Returns saved profile. */
  async saveProfile(profile: DataExchangeProfile): Promise<DataExchangeProfile> {
    const res = await fetch(`${BASE}/profiles`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(profile),
    });
    return parse<{ ok: boolean; id?: string }>(res).then((r) => ({ ...profile, id: r.id ?? profile.id }));
  },

  async deleteProfile(id: string): Promise<boolean> {
    const res = await fetch(`${BASE}/profiles/${encodeURIComponent(id)}`, { method: 'DELETE' });
    return parse<{ deleted: boolean }>(res).then((r) => !!r.deleted);
  },

  // ── Execution ───────────────────────────────────────────────────────────────
  /** POST /execute — run a profile synchronously. `input` may carry { filePath }. */
  async execute(profileId: string, input?: Record<string, unknown>): Promise<unknown> {
    const res = await fetch(`${BASE}/execute`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ profileId, input }),
    });
    return parse<unknown>(res);
  },

  // ── Monitor (file inbox execution log) ──────────────────────────────────────
  async listExecutions(limit = 50): Promise<ExecutionRecord[]> {
    const res = await fetch(`${BASE}/executions?limit=${encodeURIComponent(String(limit))}`);
    return parse<ExecutionRecord[]>(res);
  },

  async getExecution(id: string): Promise<ExecutionRecord> {
    const res = await fetch(`${BASE}/executions/${encodeURIComponent(id)}`);
    return parse<ExecutionRecord>(res);
  },
};
