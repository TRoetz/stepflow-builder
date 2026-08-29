import { describe, it, expect } from 'vitest';
import { stepSchemas, schemaById, stepLibrary, libraryById, paletteData, schemaService } from '@schemas/index';
import { categoryById } from '@schemas/categories';

describe('Schema System', () => {
  describe('Registry', () => {
    it('should have all 27 step schemas registered', () => {
      expect(stepSchemas.length).toBe(27);
    });

    it('should have all 13 categories defined', () => {
      expect(categoryById.size).toBe(13);
    });

    it('should map all schemas by ID', () => {
      for (const schema of stepSchemas) {
        expect(schemaById.get(schema.schemaId)).toBeDefined();
        expect(schemaById.get(schema.schemaId)?.schemaId).toBe(schema.schemaId);
      }
    });

    it('should have palette data for all categories', () => {
      expect(paletteData.length).toBe(13);
    });

    it('should have schema service working', () => {
      expect(schemaService.getAllSchemas().length).toBe(27);
      expect(schemaService.getCategories().length).toBe(13);
    });
  });

  describe('Schema Structure', () => {
    for (const schema of stepSchemas) {
      describe(`${schema.schemaId}`, () => {
        it('should have required fields', () => {
          expect(schema.schemaId).toBeTruthy();
          expect(schema.name).toBeTruthy();
          expect(schema.description).toBeTruthy();
          expect(schema.category).toBeTruthy();
          expect(schema.color).toBeTruthy();
          expect(schema.inputs).toBeInstanceOf(Array);
          expect(schema.outputs).toBeInstanceOf(Array);
          expect(schema.configFields).toBeInstanceOf(Array);
          expect(schema.validation).toBeInstanceOf(Array);
        });

        it('should have valid category', () => {
          const validCategories = ['ai', 'rule', 'data', 'api', 'transform', 'utility', 'subflow', 'terminal', 'flow', 'human', 'formcapture', 'remote', 'transfer'];
          expect(validCategories).toContain(schema.category);
        });

        it('should have valid inputs with data types', () => {
          const validTypes = ['json', 'string', 'number', 'boolean', 'array', 'image', 'any'];
          for (const input of schema.inputs) {
            expect(input.id).toBeTruthy();
            expect(input.label).toBeTruthy();
            expect(validTypes).toContain(input.type);
          }
        });

        it('should have valid outputs with data types', () => {
          const validTypes = ['json', 'string', 'number', 'boolean', 'array', 'image', 'any'];
          for (const output of schema.outputs) {
            expect(output.id).toBeTruthy();
            expect(output.label).toBeTruthy();
            expect(validTypes).toContain(output.type);
          }
        });

        it('should have valid config fields', () => {
          const validTypes = ['text', 'number', 'boolean', 'dropdown', 'textarea', 'code', 'json', 'file', 'color', 'slider', 'toggle', 'api-selector'];
          for (const field of schema.configFields) {
            expect(field.id).toBeTruthy();
            expect(field.label).toBeTruthy();
            expect(validTypes).toContain(field.type);
          }
        });

        it('should have valid validation rules', () => {
          for (const rule of schema.validation) {
            expect(rule.id).toBeTruthy();
            expect(rule.check).toBeInstanceOf(Function);
          }
        });
      });
    }
  });

  describe('Category Distribution', () => {
    it('should have AI schemas (2)', () => {
      expect(stepSchemas.filter((s) => s.category === 'ai').length).toBe(2);
    });

    it('should have Rule schemas (2)', () => {
      expect(stepSchemas.filter((s) => s.category === 'rule').length).toBe(2);
    });

    it('should have Data schemas (4)', () => {
      expect(stepSchemas.filter((s) => s.category === 'data').length).toBe(4);
    });

    it('should have API schemas (2)', () => {
      expect(stepSchemas.filter((s) => s.category === 'api').length).toBe(2);
    });

    it('should have Transform schemas (2)', () => {
      expect(stepSchemas.filter((s) => s.category === 'transform').length).toBe(2);
    });

    it('should have Utility schemas (3)', () => {
      expect(stepSchemas.filter((s) => s.category === 'utility').length).toBe(3);
    });

    it('should have SubFlow schemas (1)', () => {
      expect(stepSchemas.filter((s) => s.category === 'subflow').length).toBe(1);
    });

    it('should have Human schemas (1)', () => {
      expect(stepSchemas.filter((s) => s.category === 'human').length).toBe(1);
    });

    it('should have FormCapture schemas (1)', () => {
      expect(stepSchemas.filter((s) => s.category === 'formcapture').length).toBe(1);
    });

    it('should have Remote schemas (1)', () => {
      expect(stepSchemas.filter((s) => s.category === 'remote').length).toBe(1);
    });

    it('should have File Transfer schemas (1)', () => {
      expect(stepSchemas.filter((s) => s.category === 'transfer').length).toBe(1);
    });
  });

  describe('Step Library', () => {
    it('should have 6 library templates', () => {
      expect(stepLibrary.length).toBe(6);
    });

    it('should map all library entries by ID', () => {
      for (const entry of stepLibrary) {
        expect(libraryById.get(entry.id)).toBeDefined();
      }
    });

    it('should have templates across multiple categories', () => {
      const categories = new Set(stepLibrary.map((e) => e.category));
      expect(categories.size).toBeGreaterThanOrEqual(3);
    });

    it('should have valid template configurations', () => {
      for (const entry of stepLibrary) {
        expect(entry.schemaId).toBeTruthy();
        expect(entry.name).toBeTruthy();
        expect(entry.configuration).toBeDefined();
        expect(entry.tags).toBeInstanceOf(Array);
      }
    });
  });

  describe('Schema Service', () => {
    it('should search schemas by name', () => {
      const results = schemaService.searchSchemas('sql');
      expect(results.length).toBeGreaterThan(0);
    });

    it('should search schemas by tag', () => {
      const results = schemaService.searchSchemas('ai');
      expect(results.length).toBeGreaterThan(0);
    });

    it('should get schema by category', () => {
      const aiSchemas = schemaService.getSchemaByCategory('ai');
      expect(aiSchemas.length).toBe(2);
    });

    it('should get active schemas', () => {
      expect(schemaService.getActiveSchemas().length).toBeGreaterThan(0);
    });

    it('should get node component for category', () => {
      const component = schemaService.getNodeComponentForCategory('ai');
      expect(component).toBeDefined();
    });
  });
});
