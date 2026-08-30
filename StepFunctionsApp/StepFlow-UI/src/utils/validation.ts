import { NodeData, Validity, DataType } from '@schema-types/schema';
import { schemaById } from '@schemas/index';

/**
 * Check if two data types are compatible for edge connections.
 */
export function areTypesCompatible(sourceType: DataType, targetType: DataType): boolean {
  // 'any' type accepts everything
  if (sourceType === 'any' || targetType === 'any') return true;
  // Same types always compatible
  if (sourceType === targetType) return true;
  // json can flow into array, string, number, boolean
  if (sourceType === 'json' && ['array', 'string', 'number', 'boolean'].includes(targetType)) return true;
  // array can flow into json
  if (sourceType === 'array' && targetType === 'json') return true;
  // string can flow into json
  if (sourceType === 'string' && targetType === 'json') return true;
  return false;
}

/**
 * Validate a node's configuration against its schema.
 */
export function validateNode(nodeData: NodeData): Validity[] {
  const schema = schemaById.get(nodeData.schemaId as string as string);
  if (!schema) return [{ isValid: false, reason: 'Schema not found' }];

  return schema.validation.map((rule) => ({
    id: rule.id,
    ...rule.check(nodeData, new Set()),
  }));
}

/**
 * Check if a node is fully configured (all required fields filled).
 */
export function isNodeComplete(nodeData: NodeData): boolean {
  const schema = schemaById.get(nodeData.schemaId as string);
  if (!schema) return false;

  const config = nodeData.configuration || {};
  return schema.configFields.every(
    (field) => {
      if (!field.required) return true;
      if (field.condition && !field.condition(nodeData)) return true;
      const value = config[field.id];
      return value !== undefined && value !== null && value !== '';
    }
  );
}

/**
 * Get a human-readable border color for node validity.
 */
export function getValidityColor(valid: boolean, hasWarning?: boolean): string {
  if (hasWarning) return '#f59e0b'; // amber
  if (valid) return '#22c55e'; // green
  return '#ef4444'; // red
}

/**
 * Get a human-readable border color for execution status.
 */
export function getExecutionColor(status: 'idle' | 'running' | 'completed' | 'failed'): string {
  switch (status) {
    case 'running': return '#f59e0b';
    case 'completed': return '#22c55e';
    case 'failed': return '#ef4444';
    default: return '#6b7280';
  }
}
