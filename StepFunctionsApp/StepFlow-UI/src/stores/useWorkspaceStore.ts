import { create } from 'zustand';
import { WorkspaceService, type WorkspaceTree } from '@services/workspaceService';

interface WorkspaceState {
  tree: WorkspaceTree | null;
  /** "org/project/sub" of the sub-project selected as save/open target. */
  selectedSubProjectPath: string | null;
  error: string | null;

  loadTree: () => Promise<void>;
  selectSubProject: (path: string | null) => void;
}

function findSubProject(tree: WorkspaceTree, path: string): boolean {
  const target = path.toLowerCase();
  return tree.orgs.some((org) =>
    org.projects.some((project) =>
      project.subProjects.some((sub) => `${org.name}/${project.name}/${sub.name}`.toLowerCase() === target)
    )
  );
}

export const useWorkspaceStore = create<WorkspaceState>((set, get) => ({
  tree: null,
  selectedSubProjectPath: null,
  error: null,

  loadTree: async () => {
    try {
      const tree = await WorkspaceService.getTree();
      set({ tree, error: null });
      // Drop a stale selection if the node no longer exists.
      const sel = get().selectedSubProjectPath;
      if (sel && !findSubProject(tree, sel)) set({ selectedSubProjectPath: null });
    } catch (err) {
      set({ error: err instanceof Error ? err.message : String(err) });
    }
  },

  selectSubProject: (path) => set({ selectedSubProjectPath: path }),
}));
