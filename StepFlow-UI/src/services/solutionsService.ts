// ═══════════════════════════════════════════════════════════
// Solutions Service — browser client for the portable solution
// package API (test -> prod deploy path). Export bundles a workspace
// node's flows (+ canvas), forms, attribute domains, dynamic APIs,
// data-exchange profiles, named rules, EAV datasets and SQL schemas;
// import redeploys the package idempotently and returns a report.
// ═══════════════════════════════════════════════════════════

export interface SolutionManifest {
  name: string;
  version: string;
  description?: string | null;
  createdAtUtc: string;
  sourceNodePath: string;
}

export interface SolutionPackage {
  format: string;
  formatVersion: number;
  manifest: SolutionManifest;
  flows: unknown[];
  attributeDomains: unknown[];
  schemaDefinitions: unknown[];
  forms: unknown[];
  dynamicApis: unknown[];
  dataExchangeProfiles: unknown[];
  rules: unknown[];
  eavEntities: unknown[];
  eavRows: unknown[];
  dataSources: unknown[];
}

/** Import report: one array per section, each entry a small summary object. */
export type SolutionImportReport = Record<string, Array<Record<string, unknown>>>;

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body - keep the status message */ }
  throw new Error(message);
}

export interface SolutionExportOptions {
  nodePath?: string;
  seedTables?: string;
  seedDomains?: string;
  name?: string;
  version?: string;
}

export const SolutionsService = {
  /** Export the application attached to a workspace node as a portable package. */
  async exportPackage(opts: SolutionExportOptions): Promise<SolutionPackage> {
    const params = new URLSearchParams();
    if (opts.nodePath) params.set('nodePath', opts.nodePath);
    if (opts.seedTables) params.set('seedTables', opts.seedTables);
    if (opts.seedDomains) params.set('seedDomains', opts.seedDomains);
    if (opts.name) params.set('name', opts.name);
    if (opts.version) params.set('version', opts.version);
    const qs = params.toString();
    const res = await fetch(`/api/solutions/export${qs ? `?${qs}` : ''}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as SolutionPackage;
  },

  /** Import a package onto this instance (idempotent redeploy). Returns the per-section report. */
  async importPackage(pkg: SolutionPackage, targetNodePath?: string): Promise<SolutionImportReport> {
    const res = await fetch('/api/solutions/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ package: pkg, targetNodePath }),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as SolutionImportReport;
  },
};
