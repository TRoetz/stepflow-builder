import { useState, useCallback, useEffect, type ReactNode } from 'react';
import {
  FolderTree, ChevronRight, ChevronDown, Plus, Pencil, Trash2, X, Maximize2, Minimize2, Save, Shield, FileJson, AlertCircle,
} from 'lucide-react';
import {
  WorkspaceService, type AccessEntry, type EffectiveGrant, type SubProjectNode, type WorkspaceFlow,
} from '@services/workspaceService';
import { useWorkspaceStore } from '@stores/useWorkspaceStore';
import { showToast } from '@stores/useToastStore';

const ROLES = ['viewer', 'editor', 'admin', 'owner'] as const;

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

function roleChipClass(role: string): string {
  switch (role) {
    case 'editor': return 'bg-blue-500/10 text-blue-400';
    case 'admin': return 'bg-amber-500/10 text-amber-400';
    case 'owner': return 'bg-green-500/10 text-green-400';
    default: return 'bg-gray-500/10 text-gray-400';
  }
}

/** Resolves "org/project/sub" against the tree; returns the sub-project node or null. */
function findSubProjectNode(tree: { orgs: Array<{ name: string; projects: Array<{ name: string; subProjects: SubProjectNode[] }> }> } | null, path: string | null): SubProjectNode | null {
  if (!tree || !path) return null;
  const target = path.toLowerCase();
  for (const org of tree.orgs) {
    for (const project of org.projects) {
      for (const sub of project.subProjects) {
        if (`${org.name}/${project.name}/${sub.name}`.toLowerCase() === target) return sub;
      }
    }
  }
  return null;
}

interface WorkspacePanelProps {
  onClose: () => void;
  /** Loads a workspace flow into the canvas (App wires FlowService.importFlow + autoLayout). */
  onOpenFlow: (flow: WorkspaceFlow) => void;
  /** Saves the current canvas flow to the selected sub-project. */
  onSaveCurrentFlow: () => void;
}

export function WorkspacePanel({ onClose, onOpenFlow, onSaveCurrentFlow }: WorkspacePanelProps) {
  const tree = useWorkspaceStore((s) => s.tree);
  const loadTree = useWorkspaceStore((s) => s.loadTree);
  const selectedSubProjectPath = useWorkspaceStore((s) => s.selectedSubProjectPath);
  const selectSubProject = useWorkspaceStore((s) => s.selectSubProject);
  const storeError = useWorkspaceStore((s) => s.error);

  const [isMaximized, setIsMaximized] = useState(false);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  // Inline add / rename state. addingUnder: "" = root (new org), else parent node path.
  const [addingUnder, setAddingUnder] = useState<string | null>(null);
  const [addValue, setAddValue] = useState('');
  const [renamingPath, setRenamingPath] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');

  // ACL editor state (aclPath: any node — org, project or sub-project).
  const [aclPath, setAclPath] = useState<string | null>(null);
  const [localEntries, setLocalEntries] = useState<AccessEntry[]>([]);
  const [effectiveGrants, setEffectiveGrants] = useState<EffectiveGrant[]>([]);
  const [newPrincipal, setNewPrincipal] = useState('');
  const [newRole, setNewRole] = useState<string>('viewer');
  const [aclDirty, setAclDirty] = useState(false);

  // Load the tree on mount.
  useEffect(() => { loadTree(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // First time we have a tree: expand orgs + projects so sub-projects are visible.
  useEffect(() => {
    if (!tree || Object.keys(expanded).length > 0) return;
    const next: Record<string, boolean> = {};
    for (const org of tree.orgs) {
      next[org.name] = true;
      for (const project of org.projects) next[`${org.name}/${project.name}`] = true;
    }
    if (Object.keys(next).length > 0) setExpanded(next);
  }, [tree, expanded]);

  // Load the ACL whenever the selected node changes.
  useEffect(() => {
    let cancelled = false;
    setLocalEntries([]);
    setEffectiveGrants([]);
    setAclDirty(false);
    if (!aclPath) return;
    WorkspaceService.getAccess(aclPath).then((res) => {
      if (cancelled) return;
      setLocalEntries(res.local ?? []);
      setEffectiveGrants(res.effective ?? []);
    }).catch((err) => showToast({ type: 'error', message: errorMessage(err) }));
    return () => { cancelled = true; };
  }, [aclPath]);

  const refresh = useCallback(() => { loadTree(); }, [loadTree]);

  // ── Node mutations ────────────────────────────────────────────────────────────────

  const startAdding = (parentPath: string) => { setAddingUnder(parentPath); setAddValue(''); };
  const cancelAdding = () => { setAddingUnder(null); setAddValue(''); };

  const handleAddNode = async () => {
    if (addingUnder === null || !addValue.trim()) return;
    try {
      await WorkspaceService.createNode(addValue.trim(), addingUnder || undefined);
      // Expand the new parent so the child is visible.
      setExpanded((prev) => ({ ...prev, [addingUnder]: true }));
      cancelAdding();
      refresh();
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  const startRenaming = (path: string, currentName: string) => { setRenamingPath(path); setRenameValue(currentName); };

  const handleRenameNode = async () => {
    if (!renamingPath || !renameValue.trim()) return;
    try {
      await WorkspaceService.renameNode(renamingPath, renameValue.trim());
      // Keep the ACL editor pointed at the renamed node.
      setAclPath((prev) => (prev === renamingPath ? `${renamingPath.slice(0, renamingPath.lastIndexOf('/') + 1)}${renameValue.trim()}` : prev));
      setRenamingPath(null);
      refresh();
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  const handleDeleteNode = async (path: string) => {
    if (!window.confirm(`Delete "${path}" and everything under it?`)) return;
    try {
      await WorkspaceService.deleteNode(path);
      setAclPath((prev) => (prev !== null && (prev === path || prev.startsWith(path + '/')) ? null : prev));
      const sel = useWorkspaceStore.getState().selectedSubProjectPath;
      if (sel && (sel === path || sel.startsWith(path + '/'))) selectSubProject(null);
      refresh();
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  // ── Selection ─────────────────────────────────────────────────────────────────────

  const toggleExpand = (path: string) => setExpanded((prev) => ({ ...prev, [path]: !prev[path] }));

  /** Clicking a node name opens its ACL editor; sub-projects also become the save target. */
  const selectNode = (path: string, isSubProject: boolean) => {
    setAclPath((prev) => (prev === path ? null : path));
    if (isSubProject) selectSubProject(selectedSubProjectPath === path ? null : path);
  };

  // ── ACL mutations ─────────────────────────────────────────────────────────────────

  const addEntry = () => {
    const principal = newPrincipal.trim();
    if (!principal || !aclPath) return;
    setLocalEntries((prev) => [...prev, { principal, role: newRole }]);
    setNewPrincipal('');
    setAclDirty(true);
  };

  const updateEntryRole = (index: number, role: string) => {
    setLocalEntries((prev) => prev.map((e, i) => (i === index ? { ...e, role } : e)));
    setAclDirty(true);
  };

  const removeEntry = (index: number) => {
    setLocalEntries((prev) => prev.filter((_, i) => i !== index));
    setAclDirty(true);
  };

  const handleSaveAccess = async () => {
    if (!aclPath || !aclDirty) return;
    try {
      await WorkspaceService.saveAccess(aclPath, localEntries);
      const res = await WorkspaceService.getAccess(aclPath);
      setLocalEntries(res.local ?? []);
      setEffectiveGrants(res.effective ?? []);
      setAclDirty(false);
      showToast({ type: 'success', message: `Saved access for ${aclPath}` });
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  // ── Flow ops on the selected sub-project ──────────────────────────────────────────

  const selectedSub = findSubProjectNode(tree, selectedSubProjectPath);

  const handleOpenFlow = async (flowId: string) => {
    if (!selectedSubProjectPath) return;
    try {
      const flow = await WorkspaceService.getFlow(selectedSubProjectPath, flowId);
      onOpenFlow(flow);
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  const handleDeleteFlow = async (flowId: string, name: string) => {
    if (!selectedSubProjectPath || !window.confirm(`Delete flow "${name}"?`)) return;
    try {
      await WorkspaceService.deleteFlow(selectedSubProjectPath, flowId);
      refresh();
    } catch (err) { showToast({ type: 'error', message: errorMessage(err) }); }
  };

  // ── Render helpers ────────────────────────────────────────────────────────────────

  const renderAddRow = (indentClass: string, placeholder: string) => (
    <div className={`flex items-center gap-1.5 py-0.5 ${indentClass}`}>
      <input
        autoFocus
        value={addValue}
        onChange={(e) => setAddValue(e.target.value)}
        onKeyDown={(e) => { if (e.key === 'Enter') handleAddNode(); if (e.key === 'Escape') cancelAdding(); }}
        onBlur={() => addValue.trim() ? handleAddNode() : cancelAdding()}
        placeholder={placeholder}
        className="flex-1 min-w-0 bg-gray-800 border border-indigo-500/40 rounded px-2 py-0.5 text-xs text-gray-200 focus:outline-none"
      />
    </div>
  );

  const renderNodeRow = (opts: {
    path: string; name: string; level: number; isSubProject?: boolean; selected?: boolean; badges?: ReactNode;
  }) => {
    const { path, name, level, isSubProject = false, selected = false, badges } = opts;
    const indentClass = level === 0 ? 'pl-2' : level === 1 ? 'pl-7' : 'pl-12';
    return (
      <div className={`group flex items-center gap-1 py-1 rounded hover:bg-gray-800/50 ${indentClass} ${selected ? 'bg-indigo-500/10 ring-1 ring-indigo-500/30' : ''}`}>
        {level < 2 && (
          <button className="text-gray-500 hover:text-gray-300 shrink-0" onClick={() => toggleExpand(path)} title={expanded[path] ? 'Collapse' : 'Expand'}>
            {expanded[path] ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
          </button>
        )}
        {level === 2 && <span className="w-3.5 shrink-0" />}
        {renamingPath === path ? (
          <input
            autoFocus
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') handleRenameNode(); if (e.key === 'Escape') setRenamingPath(null); }}
            onBlur={() => renameValue.trim() ? handleRenameNode() : setRenamingPath(null)}
            className="flex-1 min-w-0 bg-gray-800 border border-indigo-500/40 rounded px-2 py-0.5 text-xs text-gray-200 focus:outline-none"
          />
        ) : (
          <span
            onClick={() => selectNode(path, isSubProject)}
            title={isSubProject ? 'Select as save target + edit access' : 'Edit access'}
            className={`flex-1 min-w-0 truncate text-xs cursor-pointer ${level === 0 ? 'font-semibold text-gray-200' : level === 1 ? 'font-medium text-gray-300' : 'text-gray-400'} hover:text-indigo-300`}
          >
            {name}
          </span>
        )}
        {badges}
        <div className="hidden group-hover:flex items-center gap-0.5 shrink-0">
          {level === 2 ? (
            <>
              <button className="p-1 text-gray-500 hover:text-indigo-300" title="Rename" onClick={() => startRenaming(path, name)}><Pencil className="w-3 h-3" /></button>
              <button className="p-1 text-gray-500 hover:text-red-400" title="Delete" onClick={() => handleDeleteNode(path)}><Trash2 className="w-3 h-3" /></button>
            </>
          ) : (
            <>
              <button className="p-1 text-gray-500 hover:text-indigo-300" title={level === 0 ? 'Add project' : 'Add sub-project'} onClick={() => startAdding(path)}><Plus className="w-3 h-3" /></button>
              <button className="p-1 text-gray-500 hover:text-indigo-300" title="Rename" onClick={() => startRenaming(path, name)}><Pencil className="w-3 h-3" /></button>
              <button className="p-1 text-gray-500 hover:text-red-400" title="Delete" onClick={() => handleDeleteNode(path)}><Trash2 className="w-3 h-3" /></button>
            </>
          )}
        </div>
      </div>
    );
  };

  // ── Render ────────────────────────────────────────────────────────────────────────

  return (
    <div className={isMaximized ? 'fixed inset-0 z-50 flex flex-col bg-gray-900' : 'w-[560px] shrink-0 flex flex-col h-full bg-gray-900 border-l border-gray-800'}>
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
        <h2 className="text-sm font-semibold text-gray-100 flex items-center gap-2">
          <FolderTree className="w-4 h-4 text-indigo-400" />
          Workspace
        </h2>
        <div className="flex items-center gap-1">
          <button className="btn-icon" title={isMaximized ? 'Restore' : 'Expand'} onClick={() => setIsMaximized(!isMaximized)}>
            {isMaximized ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
          </button>
          <button className="btn-icon" title="Close Workspace Panel" onClick={onClose}>
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto">
        {storeError && (
          <div className="flex items-center gap-2 px-4 py-2 bg-red-500/10 border-b border-red-500/30 text-xs text-red-400">
            <AlertCircle className="w-3.5 h-3.5 shrink-0" /> {storeError}
          </div>
        )}

        {/* Tree */}
        <div className="p-2">
          <div className="flex items-center justify-between px-2 py-1">
            <span className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold">Organizations</span>
            {addingUnder !== '' && (
              <button className="p-1 text-gray-500 hover:text-indigo-300" title="Add organization" onClick={() => startAdding('')}>
                <Plus className="w-3.5 h-3.5" />
              </button>
            )}
          </div>

          {addingUnder === '' && renderAddRow('pl-2', 'Organization name')}

          {!tree || tree.orgs.length === 0 ? (
            <div className="text-xs text-gray-600 px-4 py-3">No organizations yet — add one to get started.</div>
          ) : (
            tree.orgs.map((org) => (
              <div key={org.name}>
                {renderNodeRow({ path: org.name, name: org.name, level: 0 })}
                {addingUnder === org.name && renderAddRow('pl-7', 'Project name')}
                {expanded[org.name] && (
                  <>
                    {org.projects.length === 0 && <div className="text-[11px] text-gray-600 pl-12 py-0.5">No projects</div>}
                    {org.projects.map((project) => (
                      <div key={`${org.name}/${project.name}`}>
                        {renderNodeRow({ path: `${org.name}/${project.name}`, name: project.name, level: 1 })}
                        {addingUnder === `${org.name}/${project.name}` && renderAddRow('pl-12', 'Sub-project name')}
                        {expanded[`${org.name}/${project.name}`] && (
                          <>
                            {project.subProjects.length === 0 && <div className="text-[11px] text-gray-600 pl-12 py-0.5">No sub-projects</div>}
                            {project.subProjects.map((sub) => {
                              const subPath = `${org.name}/${project.name}/${sub.name}`;
                              return (
                                <div key={subPath}>
                                  {renderNodeRow({
                                    path: subPath, name: sub.name, level: 2, isSubProject: true,
                                    selected: selectedSubProjectPath === subPath,
                                    badges: (
                                      <span className="flex items-center gap-1 shrink-0 text-[10px] text-gray-500">
                                        {sub.flows.length > 0 && <span className="bg-gray-800 rounded px-1.5 py-0.5">{sub.flows.length} flow{sub.flows.length === 1 ? '' : 's'}</span>}
                                        {sub.profileIds.length > 0 && <span className="bg-gray-800 rounded px-1.5 py-0.5">{sub.profileIds.length} profile{sub.profileIds.length === 1 ? '' : 's'}</span>}
                                      </span>
                                    )
                                  })}
                                </div>
                              );
                            })}
                          </>
                        )}
                      </div>
                    ))}
                  </>
                )}
              </div>
            ))
          )}
        </div>

        {/* ACL editor for the selected node */}
        {aclPath && (
          <div className="border-t border-gray-800">
            <div className="flex items-center justify-between px-4 py-2 bg-gray-950/40">
              <div className="flex items-center gap-1.5 text-xs font-semibold text-gray-300 min-w-0">
                <Shield className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
                <span className="truncate">Access — {aclPath}</span>
              </div>
              <button className="btn-icon" title="Close access editor" onClick={() => setAclPath(null)}>
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
            <div className="p-3 space-y-4">
              {/* Local entries */}
              <div>
                <div className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold mb-1.5">Local grants (access.json)</div>
                {localEntries.length === 0 ? (
                  <div className="text-xs text-gray-600">No local entries — effective access is inherited from ancestors.</div>
                ) : (
                  <div className="space-y-1">
                    {localEntries.map((entry, i) => (
                      <div key={`${entry.principal}-${i}`} className="flex items-center gap-2">
                        <span className="flex-1 min-w-0 truncate text-xs text-gray-300 font-mono">{entry.principal}</span>
                        <select
                          value={entry.role}
                          onChange={(e) => updateEntryRole(i, e.target.value)}
                          className="bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-xs text-gray-300 focus:outline-none"
                        >
                          {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                        </select>
                        <button className="p-1 text-gray-500 hover:text-red-400" title="Remove entry" onClick={() => removeEntry(i)}>
                          <Trash2 className="w-3 h-3" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
                {/* Add entry row */}
                <div className="flex items-center gap-1.5 mt-2">
                  <input
                    value={newPrincipal}
                    onChange={(e) => setNewPrincipal(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') addEntry(); }}
                    placeholder="principal (user name)"
                    className="flex-1 min-w-0 bg-gray-800 border border-gray-700 rounded px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-indigo-500/40"
                  />
                  <select value={newRole} onChange={(e) => setNewRole(e.target.value)} className="bg-gray-800 border border-gray-700 rounded px-1.5 py-1 text-xs text-gray-300 focus:outline-none">
                    {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                  </select>
                  <button className="btn-icon" title="Add entry" onClick={addEntry}><Plus className="w-4 h-4" /></button>
                </div>
              </div>

              {/* Effective grants */}
              <div>
                <div className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold mb-1.5">Effective grants (inherited + local)</div>
                {effectiveGrants.length === 0 ? (
                  <div className="text-xs text-gray-600">No grants along this path.</div>
                ) : (
                  <div className="space-y-1">
                    {effectiveGrants.map((grant) => (
                      <div key={grant.principal} className="flex items-center gap-2">
                        <span className="flex-1 min-w-0 truncate text-xs text-gray-300 font-mono">{grant.principal}</span>
                        <span className={`text-[10px] rounded px-1.5 py-0.5 ${roleChipClass(grant.role)}`}>{grant.role}</span>
                        <span className="text-[10px] text-gray-600 shrink-0">
                          {grant.sourcePath.toLowerCase() === aclPath!.toLowerCase() ? 'local' : `inherited from ${grant.sourcePath}`}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <button
                disabled={!aclDirty}
                onClick={handleSaveAccess}
                className="btn btn-primary w-full justify-center gap-1.5 text-xs bg-indigo-600 hover:bg-indigo-500 border-indigo-500/20 disabled:opacity-40"
              >
                <Save className="w-3.5 h-3.5" /> Save Access
              </button>
            </div>
          </div>
        )}

        {/* Flows of the selected sub-project */}
        {selectedSub && selectedSubProjectPath && (
          <div className="border-t border-gray-800">
            <div className="flex items-center justify-between px-4 py-2 bg-gray-950/40">
              <div className="text-xs font-semibold text-gray-300 truncate">Flows — {selectedSubProjectPath}</div>
              <span className="text-[10px] text-gray-600 shrink-0 ml-2">{selectedSub.flows.length} flow{selectedSub.flows.length === 1 ? '' : 's'}</span>
            </div>
            <div className="p-3 space-y-2">
              <button onClick={onSaveCurrentFlow} className="btn btn-primary w-full justify-center gap-1.5 text-xs bg-indigo-600 hover:bg-indigo-500 border-indigo-500/20">
                <Save className="w-3.5 h-3.5" /> Save current canvas flow here
              </button>

              {selectedSub.flows.length === 0 ? (
                <div className="text-xs text-gray-600">No flows saved in this sub-project yet.</div>
              ) : (
                selectedSub.flows.map((flow) => (
                  <div key={flow.id} className="flex items-center gap-2 bg-gray-800/40 border border-gray-800 rounded px-2.5 py-1.5">
                    <FileJson className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
                    <div className="flex-1 min-w-0">
                      <div className="text-xs text-gray-200 truncate">{flow.name}</div>
                      <div className="text-[10px] text-gray-600 font-mono truncate">{flow.id}</div>
                    </div>
                    <button className="btn btn-secondary px-2 py-1 text-[11px]" title="Open in canvas" onClick={() => handleOpenFlow(flow.id)}>Open</button>
                    <button className="p-1 text-gray-500 hover:text-red-400" title="Delete flow" onClick={() => handleDeleteFlow(flow.id, flow.name)}>
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </div>
                ))
              )}

              {selectedSub.profileIds.length > 0 && (
                <div className="pt-1">
                  <div className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold mb-1.5">Profiles</div>
                  <div className="flex flex-wrap gap-1">
                    {selectedSub.profileIds.map((id) => (
                      <span key={id} className="bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[10px] font-mono text-gray-400">{id}</span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Unassigned profiles */}
        {tree && tree.unassignedProfiles.length > 0 && (
          <div className="border-t border-gray-800 p-3">
            <div className="text-[10px] uppercase tracking-wider text-gray-500 font-semibold mb-1.5">Unassigned profiles</div>
            <p className="text-[10px] text-gray-600 mb-1.5">Not under any sub-project (legacy / unassigned bucket).</p>
            <div className="flex flex-wrap gap-1">
              {tree.unassignedProfiles.map((id) => (
                <span key={id} className="bg-gray-800 border border-gray-700 rounded px-1.5 py-0.5 text-[10px] font-mono text-gray-400">{id}</span>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
