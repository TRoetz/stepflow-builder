import { useCallback } from 'react';
import ELK from 'elkjs';
import { useNodeStore, StepNode } from '@stores/useNodeStore';

/**
 * Hook for auto-layout using ELKJS (same as chaiNNer).
 * Provides a function to layout nodes on the canvas.
 */
export function useAutoLayout() {
  const autoLayout = useCallback(async () => {
    const nodes = useNodeStore.getState().nodes;
    const edges = useEdgeStore.getState().edges;

    if (nodes.length === 0) return;

    const elk = new ELK();

    // Build ELK graph
    const elkGraph = {
      id: 'root',
      layoutOptions: {
        'elk.algorithm': 'layered',
        'elk.layered.spacing.edgeNodeBetweenLayers': '100',
        'elk.layered.spacing.nodeNodeBetweenLayers': '80',
        'elk.spacing.nodeType': '40',
        'elk.spacing.edgeNode': '30',
        'elk.layered.crossingMinimization.semiInteractive': 'true',
        'elk.layered.nodePlacement.bkk.criterion': 'LAYER_SIZE',
      },
      children: nodes.map((node) => ({
        id: node.id,
        width: node.measured?.width || 240,
        height: node.measured?.height || 100,
        targets: [],
      })),
      edges: edges.map((edge) => ({
        id: `e-${edge.id}`,
        sources: [edge.source],
        targets: [edge.target],
      })),
    };

    try {
      const layout = await elk.layout(elkGraph);

      // Apply layout positions to nodes
      const updatedNodes: StepNode[] = nodes.map((node) => {
        const layoutNode = layout.children?.find((c) => c.id === node.id);
        if (layoutNode?.x && layoutNode?.y) {
          return {
            ...node,
            position: {
              x: layoutNode.x - (layoutNode.width || 240) / 2,
              y: layoutNode.y - (layoutNode.height || 100) / 2,
            },
            positionAbsolute: {
              x: layoutNode.x - (layoutNode.width || 240) / 2,
              y: layoutNode.y - (layoutNode.height || 100) / 2,
            },
          };
        }
        return node;
      });

      useNodeStore.setState({ nodes: updatedNodes });
    } catch (error) {
      console.error('Auto-layout failed:', error);
    }
  }, []);

  return { autoLayout };
}

// Import here to avoid circular deps
import { useEdgeStore } from '@stores/useEdgeStore';
