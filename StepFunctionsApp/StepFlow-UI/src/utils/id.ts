import { v4 as uuidv4 } from 'uuid';

/**
 * Generate a unique ID for nodes, edges, etc.
 */
export function generateId(prefix?: string): string {
  return prefix ? `${prefix}-${uuidv4().slice(0, 8)}` : uuidv4();
}

/**
 * Generate a node-specific ID with schema info.
 */
export function generateNodeId(schemaId: string): string {
  return `node-${schemaId.replace(/:/g, '-')}-${uuidv4().slice(0, 8)}`;
}

/**
 * Generate an edge ID.
 */
export function generateEdgeId(sourceId: string, targetId: string): string {
  return `edge-${sourceId.slice(0, 8)}-${targetId.slice(0, 8)}-${uuidv4().slice(0, 4)}`;
}
