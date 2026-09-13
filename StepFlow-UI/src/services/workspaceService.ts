// ============================================================================
// Workspace Service Client
// Talks to /api/workspace endpoints: org -> project -> sub-project tree with
// per-node ACLs, plus flow access under a selected sub-project.
// Node paths are "org/project/sub" (forward slashes) — identical to the on-disk layout.
// ============================================================================

import type { CanvasMetadata } from './flowService';

export interface AccessEntry {
  principal: string;
  role: string; // viewer | editor | admin | owner
}

/** A grant after merging ACLs along org -> project -> sub-project (higher rank wins). */
export interface EffectiveGrant extends AccessEntry {
  /** Node path whose local ACL supplied the winning entry — labels inherited vs local grants. */
  sourcePath: string;
}

export interface FlowRef {
  id: string;
  name: string;
}

export interface SubProjectNode {
  name: string;
  flows: FlowRef[];
  profileIds: string[];
}

export interface ProjectNode {
  name: string;
  subProjects: SubProjectNode[];
}

export interface OrgNode {
  name: string;
  projects: ProjectNode[];
}

/** Full workspace tree as returned by GET /api/workspace. */
export interface WorkspaceTree {
  orgs: OrgNode[];
  /** Profile ids not found under any sub-project (unassigned bucket + legacy leftovers). */
  unassignedProfiles: string[];
}

export interface WorkspaceFlowMeta {
  id: string;
  name: string;
  description?: string | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

/** A stored flow document: camelCase ASL { startAt, states } exactly as the UI exports it. */
export interface WorkspaceFlow extends WorkspaceFlowMeta {
  definition: Record<string, unknown>;
}

async function fail(res: Response): Promise<never> {
  let message = `Request failed with status ${res.status}`;
  try {
    const body = (await res.json()) as { error?: string };
    if (body && typeof body.error === 'string' && body.error.length > 0) message = body.error;
  } catch { /* non-JSON error body — keep the status message */ }
  throw new Error(message);
}

export class WorkspaceService {
  /** Full tree: orgs -> projects -> sub-projects (each with flow refs + profile ids). */
  static async getTree(): Promise<WorkspaceTree> {
    const res = await fetch('/api/workspace');
    if (!res.ok) return fail(res);
    return (await res.json()) as WorkspaceTree;
  }

  /** Creates a node under parentPath (or at the root when omitted). Returns the new path. */
  static async createNode(name: string, parentPath?: string): Promise<string> {
    const res = await fetch('/api/workspace/nodes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, ...(parentPath ? { parentPath } : {}) }),
    });
    if (!res.ok) return fail(res);
    const body = (await res.json()) as { path?: string };
    return body.path ?? '';
  }

  /** Renames a node in place; children + ACLs move with it. Returns the new path. */
  static async renameNode(path: string, newName: string): Promise<string> {
    const res = await fetch('/api/workspace/nodes/rename', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path, newName }),
    });
    if (!res.ok) return fail(res);
    const body = (await res.json()) as { path?: string };
    return body.path ?? '';
  }

  /** Recursively deletes a node and everything under it. */
  static async deleteNode(path: string): Promise<void> {
    const res = await fetch(`/api/workspace/nodes?path=${encodeURIComponent(path)}`, { method: 'DELETE' });
    if (!res.ok) return fail(res);
  }

  /** Local + effective grants for a node. */
  static async getAccess(path: string): Promise<{ local: AccessEntry[]; effective: EffectiveGrant[] }> {
    const res = await fetch(`/api/workspace/access?path=${encodeURIComponent(path)}`);
    if (!res.ok) return fail(res);
    return (await res.json()) as { local: AccessEntry[]; effective: EffectiveGrant[] };
  }

  /** Replaces a node's local ACL. Returns the saved local entries. */
  static async saveAccess(path: string, entries: AccessEntry[]): Promise<AccessEntry[]> {
    const res = await fetch(`/api/workspace/access?path=${encodeURIComponent(path)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ entries }),
    });
    if (!res.ok) return fail(res);
    const body = (await res.json()) as { local?: AccessEntry[] };
    return body.local ?? [];
  }

  // ── Flows under a sub-project (subProjectPath = "org/project/sub") ────────────────

  private static flowsUrl(subProjectPath: string, flowId?: string): string {
    const [org, project, sub] = subProjectPath.split('/');
    const base = `/api/workspace/subprojects/${encodeURIComponent(org)}/${encodeURIComponent(project)}/${encodeURIComponent(sub)}/flows`;
    return flowId ? `${base}/${encodeURIComponent(flowId)}` : base;
  }

  /** Lists flows of a sub-project (id/name/description/timestamps). */
  static async listFlows(subProjectPath: string): Promise<WorkspaceFlowMeta[]> {
    const res = await fetch(WorkspaceService.flowsUrl(subProjectPath));
    if (!res.ok) return fail(res);
    return (await res.json()) as WorkspaceFlowMeta[];
  }

  /** Gets a single flow document ({ startAt, states }) with its metadata. */
  static async getFlow(subProjectPath: string, flowId: string): Promise<WorkspaceFlow> {
    const res = await fetch(WorkspaceService.flowsUrl(subProjectPath, flowId));
    if (!res.ok) return fail(res);
    return (await res.json()) as WorkspaceFlow;
  }

  /** Saves a flow to a sub-project. Payload mirrors POST /api/flows. Returns { id, created }. */
  static async saveFlow(
    subProjectPath: string,
    payload: { name: string; description?: string; id?: string; startAt?: string; states: Record<string, unknown>; canvas?: CanvasMetadata }
  ): Promise<{ id: string; created: boolean }> {
    const res = await fetch(WorkspaceService.flowsUrl(subProjectPath), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    if (!res.ok) return fail(res);
    return (await res.json()) as { id: string; created: boolean };
  }

  /** Deletes a flow from a sub-project. */
  static async deleteFlow(subProjectPath: string, flowId: string): Promise<void> {
    const res = await fetch(WorkspaceService.flowsUrl(subProjectPath, flowId), { method: 'DELETE' });
    if (!res.ok) return fail(res);
  }
}
