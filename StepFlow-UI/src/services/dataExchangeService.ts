// ============================================================================
// Data Exchange Service Client
// Talks to /api/data-exchange endpoints for profile CRUD and execution monitoring.
// Profiles are free-form JSON documents on the wire (camelCase .NET model); this
// client only normalizes list items into { id, name } for display purposes.
// ============================================================================


export interface DataExchangeProfile {
  dataExchangeProfileName?: string;
  profileId?: string | null;
  flowId?: string | null;
  isActive?: boolean;
  dataSource?: Record<string, unknown> | null;
  pipeline?: Record<string, unknown> | null;
  [key: string]: unknown;
}

/** Normalized list entry: stable id + display name over the raw wire document. */
export interface ProfileListItem {
  id: string;
  name: string;
  profile: DataExchangeProfile;
}

export interface ExecutionAction {
  name?: string;
  type?: string;
  messages?: unknown[];
  [key: string]: unknown;
}

export interface ExecutionStage {
  pipeline?: string;
  stageType?: string | number;
  order?: number;
  actions?: ExecutionAction[];
  [key: string]: unknown;
}

export interface DispatchResult {
  endpoint?: string;
  method?: string;
  rows?: number;
  ok?: number;
  failed?: number;
  statuses?: number[];
  [key: string]: unknown;
}

/** Execution artifact as written by the executor (success) or file monitor (failure). */
export interface ExecutionRecord {
  executionId?: string;
  success?: boolean;
  profileId?: string | null;
  source?: string | null;
  sourceFile?: string | null;
  error?: string | null;
  rowsIn?: number;
  rowsOut?: number;
  rejectedCount?: number;
  rejected?: unknown[];
  enrichedRows?: Record<string, unknown>[];
  stages?: ExecutionStage[];
  dispatched?: DispatchResult[];
  [key: string]: unknown;
}

/** Mirrors DataExchangeProfileStore.ResolveId/Slug on the backend. */
function resolveId(p: DataExchangeProfile): string {
  if (p.profileId && p.profileId.trim().length > 0) {
    return p.profileId.replace(/[^A-Za-z0-9._-]/g, '');
  }
  const slug = (p.dataExchangeProfileName ?? 'profile')
    .replace(/[^A-Za-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase();
  return slug || 'profile';
}

export class DataExchangeService {
  /** List all profiles with normalized id/name for display. */
  static async listProfiles(): Promise<ProfileListItem[]> {
    const res = await fetch(`/api/data-exchange/profiles`);
    if (!res.ok) throw new Error(`Failed to load profiles: ${res.status}`);
    const raw = (await res.json()) as DataExchangeProfile[];
    return raw.map((p) => ({
      id: resolveId(p),
      name: p.dataExchangeProfileName || '(unnamed)',
      profile: p,
    }));
  }

  /** Fetch a single profile's raw wire document by id (or name). */
  static async getProfile(id: string): Promise<DataExchangeProfile> {
    const res = await fetch(`/api/data-exchange/profiles/${encodeURIComponent(id)}`);
    if (!res.ok) throw new Error(`Failed to load profile "${id}": ${res.status}`);
    return (await res.json()) as DataExchangeProfile;
  }

  /** Save a profile document. Returns the backend-assigned id. */
  static async saveProfile(profile: DataExchangeProfile): Promise<{ id?: string }> {
    const res = await fetch(`/api/data-exchange/profiles`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(profile),
    });
    if (!res.ok) throw new Error(`Failed to save profile: ${res.status}`);
    return (await res.json()) as { id?: string };
  }

  /** Delete a profile by id. */
  static async deleteProfile(id: string): Promise<void> {
    const res = await fetch(`/api/data-exchange/profiles/${encodeURIComponent(id)}`, {
      method: 'DELETE',
    });
    if (!res.ok) throw new Error(`Failed to delete profile "${id}": ${res.status}`);
  }

  /** Execute a profile synchronously. Body contract: { "profileId", "input"? }. */
  static async execute(profileId: string): Promise<Record<string, unknown>> {
    const res = await fetch(`/api/data-exchange/execute`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ profileId }),
    });
    if (!res.ok) throw new Error(`Execution failed: ${res.status}`);
    return (await res.json()) as Record<string, unknown>;
  }

  /** List recent execution artifacts for the monitor view. */
  static async listExecutions(limit = 50): Promise<ExecutionRecord[]> {
    const res = await fetch(`/api/data-exchange/executions?limit=${limit}`);
    if (!res.ok) throw new Error(`Failed to load executions: ${res.status}`);
    return (await res.json()) as ExecutionRecord[];
  }
}
