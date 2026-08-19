// ═══════════════════════════════════════════════════════════
// Hooks for the variable engine — canvas-aware in-scope variables
// and Map-loop row scope discovery for EAV nodes.
// ═══════════════════════════════════════════════════════════

import { useMemo } from 'react';
import { useNodeStore } from '@stores/useNodeStore';
import { useEdgeStore } from '@stores/useEdgeStore';
import {
  collectInScopeVariables,
  findSubflowRowFields,
  getActiveFlowId,
  InScopeVariable,
  RowScopeInfo,
} from '@utils/variables';

/** All variables referenceable in the config of `nodeId` (upstream nodes). */
export function useInScopeVariables(nodeId: string | null): InScopeVariable[] {
  const nodes = useNodeStore((s) => s.nodes);
  const edges = useEdgeStore((s) => s.edges);

  return useMemo(() => {
    if (!nodeId) return [];
    return collectInScopeVariables(nodeId, nodes, edges);
  }, [nodeId, nodes, edges]);
}

/** Row columns available to an EAV node inside the currently open Map loop. */
export function useSubflowRowFields(): RowScopeInfo | null {
  const activeFlowId = getActiveFlowId();

  return useMemo(
    () => (activeFlowId ? findSubflowRowFields(activeFlowId) : null),
    [activeFlowId]
  );
}
