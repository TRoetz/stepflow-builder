// ── Merged flow catalog ──
// Flows live in two places: browser-local (localStorage, written by plain Save) and the
// selected Workspace sub-project (backend, written by Workspace Save). The Load dialog must
// show both, otherwise a user cannot find a flow they saved through the other path.
import { FlowService } from './flowService';
import { WorkspaceService } from './workspaceService';
import { useWorkspaceStore } from '../stores/useWorkspaceStore';

export interface CatalogEntry {
  id: string;
  name: string;
  description?: string | null;
  createdAt?: string | null;
  /** Which persistence layer holds this flow; ids are namespaced per source. */
  source: 'browser' | 'workspace';
  /** Workspace node path ("org/project/sub") when source === 'workspace'. */
  path?: string;
}

export interface FlowCatalog {
  flows: CatalogEntry[];
  /** Non-null when a workspace target is selected but its listing failed. */
  workspaceError?: string;
}

/** Lists browser-saved flows plus, when a workspace sub-project is selected, its server-side flows. */
export async function listMergedFlows(): Promise<FlowCatalog> {
  const local = await FlowService.listFlows();
  const flows: CatalogEntry[] = local.map((f) => ({
    id: f.id,
    name: f.name,
    description: f.description,
    createdAt: f.createdAt,
    source: 'browser',
  }));

  const workspaceError = undefined;
  const path = useWorkspaceStore.getState().selectedSubProjectPath;
  if (path) {
    try {
      const remote = await WorkspaceService.listFlows(path);
      for (const wf of remote) {
        flows.push({
          id: wf.id,
          name: wf.name,
          description: wf.description,
          createdAt: wf.createdAt ?? wf.updatedAt,
          source: 'workspace',
          path,
        });
      }
    } catch (err) {
      return {
        flows,
        workspaceError: err instanceof Error ? err.message : String(err),
      };
    }
  }
  return { flows, workspaceError };
}
