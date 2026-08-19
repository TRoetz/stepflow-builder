import { describe, it, expect } from 'vitest';
import { areTypesCompatible, isNodeComplete, validateNode } from '@utils/validation';
import { NodeData } from '@schema-types/schema';

describe('Validation', () => {
  describe('areTypesCompatible', () => {
    it('should allow any type with any type', () => {
      expect(areTypesCompatible('any', 'json')).toBe(true);
      expect(areTypesCompatible('json', 'any')).toBe(true);
      expect(areTypesCompatible('any', 'any')).toBe(true);
    });

    it('should allow same types', () => {
      expect(areTypesCompatible('json', 'json')).toBe(true);
      expect(areTypesCompatible('string', 'string')).toBe(true);
      expect(areTypesCompatible('number', 'number')).toBe(true);
    });

    it('should allow json to flow into primitives', () => {
      expect(areTypesCompatible('json', 'array')).toBe(true);
      expect(areTypesCompatible('json', 'string')).toBe(true);
      expect(areTypesCompatible('json', 'number')).toBe(true);
      expect(areTypesCompatible('json', 'boolean')).toBe(true);
    });

    it('should allow array to flow into json', () => {
      expect(areTypesCompatible('array', 'json')).toBe(true);
    });

    it('should allow string to flow into json', () => {
      expect(areTypesCompatible('string', 'json')).toBe(true);
    });

    it('should reject incompatible types', () => {
      expect(areTypesCompatible('number', 'string')).toBe(false);
      expect(areTypesCompatible('boolean', 'image')).toBe(false);
    });
  });

  describe('validateNode', () => {
    it('should return error when schema is missing', () => {
      const result = validateNode({
        schemaId: 'nonexistent',
        category: 'utility',
        configuration: {},
      } as NodeData);
      expect(result.length).toBeGreaterThan(0);
      expect(result[0].isValid).toBe(false);
    });

    it('should validate a valid node', () => {
      const result = validateNode({
        schemaId: 'stepflow:utility:pass',
        category: 'utility',
        configuration: {},
      } as NodeData);
      // All validation rules should pass
      const allValid = result.every((r) => r.isValid);
      expect(allValid).toBe(true);
    });
  });

  describe('isNodeComplete', () => {
    it('should return false for missing schema', () => {
      expect(isNodeComplete({
        schemaId: 'nonexistent',
        category: 'utility',
        configuration: {},
      } as NodeData)).toBe(false);
    });

    it('should return true for complete node', () => {
      expect(isNodeComplete({
        schemaId: 'stepflow:utility:pass',
        category: 'utility',
        configuration: {},
      } as NodeData)).toBe(true);
    });

    it('should return false for incomplete required fields', () => {
      // HTTP node requires url
      expect(isNodeComplete({
        schemaId: 'stepflow:api:http',
        category: 'api',
        configuration: {},
      } as NodeData)).toBe(false);
    });

    it('should return true when required fields are filled', () => {
      expect(isNodeComplete({
        schemaId: 'stepflow:api:http',
        category: 'api',
        configuration: { url: 'http://example.com', method: 'GET' },
      } as NodeData)).toBe(true);
    });
  });
});
