import { useMemo } from 'react';
import { NodeData, Validity } from '@schema-types/schema';
import { schemaById } from '@schemas/index';

/**
 * Hook to validate a node's configuration against its schema rules.
 * Returns an array of validity results (one per validation rule).
 */
export function useNodeValidity(nodeData: NodeData | undefined): {
  allValid: boolean;
  results: Validity[];
  invalidCount: number;
  errors: string[];
} {
  return useMemo(() => {
    if (!nodeData?.schemaId) {
      return { allValid: true, results: [], invalidCount: 0, errors: [] };
    }

    const schema = schemaById.get(nodeData.schemaId as string);
    if (!schema || schema.validation.length === 0) {
      return { allValid: true, results: [], invalidCount: 0, errors: [] };
    }

    // Run all validation rules
    const results: Validity[] = schema.validation.map((rule) => ({
      id: rule.id,
      ...rule.check(nodeData, new Set()),
    }));

    const invalidResults = results.filter((r) => !r.isValid);
    const allValid = invalidResults.length === 0;

    return {
      allValid,
      results,
      invalidCount: invalidResults.length,
      errors: invalidResults.map((r) => r.reason || 'Validation failed').filter(Boolean),
    };
  }, [nodeData]);
}

/**
 * Check if an edge connection is type-compatible.
 */
export function useEdgeTypeCheck() {
  return useMemo(() => {
    const check = (sourceType: string, targetType: string): boolean => {
      // 'any' type accepts everything
      if (sourceType === 'any' || targetType === 'any') return true;
      // Same types always compatible
      if (sourceType === targetType) return true;
      // json can flow into array, string, number, boolean
      if (sourceType === 'json' && ['array', 'string', 'number', 'boolean'].includes(targetType)) return true;
      // array can flow into json
      if (sourceType === 'array' && targetType === 'json') return true;
      return false;
    };
    return check;
  }, []);
}
