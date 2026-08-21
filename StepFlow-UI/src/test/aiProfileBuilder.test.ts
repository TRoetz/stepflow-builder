import { describe, it, expect } from 'vitest';
import { extractProfileJson } from '@services/aiProfileBuilder';
import { parseProfile, inferAttributesFromSample } from '@components/DataExchange/SchemaEditor';

describe('extractProfileJson', () => {
  const profile = { dataExchangeProfileName: 'P1', isActive: true };

  it('parses a bare JSON object', () => {
    expect(extractProfileJson(JSON.stringify(profile))).toEqual(profile);
  });

  it('extracts from a fenced code block with surrounding prose', () => {
    const raw = `Here you go:\n\`\`\`json\n${JSON.stringify(profile)}\n\`\`\`\nLet me know if you need changes.`;
    expect(extractProfileJson(raw)).toEqual(profile);
  });

  it('slices from first { to last } when prose wraps the object', () => {
    const raw = `Sure! ${JSON.stringify(profile)} hope that helps`;
    expect(extractProfileJson(raw)).toEqual(profile);
  });

  it('throws on empty response', () => {
    expect(() => extractProfileJson('   ')).toThrow(/empty response/);
  });

  it('throws when no JSON object is present', () => {
    expect(() => extractProfileJson('I cannot help with that.')).toThrow(/no JSON object/);
    expect(() => extractProfileJson('[1,2,3]')).toThrow(/no JSON object/);
  });

  it('throws on invalid JSON', () => {
    expect(() => extractProfileJson('{not json}')).toThrow(/Invalid JSON/);
  });

  it('rejects non-object roots (arrays)', () => {
    expect(() => extractProfileJson('```json\n[1,2]\n```')).toThrow(/not a JSON object/);
  });
});

describe('parseProfile', () => {
  const doc = { dataExchangeProfileName: 'Customer Orders Import', pipeline: { pipelineStages: [] } };

  it('accepts a valid profile document', () => {
    expect(parseProfile(JSON.stringify(doc))).toEqual(doc);
  });

  it('returns null for invalid JSON', () => {
    expect(parseProfile('{oops')).toBeNull();
  });

  it('returns null for non-object roots', () => {
    expect(parseProfile('[1,2]')).toBeNull();
  });

  it('returns null when dataExchangeProfileName is missing or empty', () => {
    expect(parseProfile(JSON.stringify({ pipeline: {} }))).toBeNull();
    expect(parseProfile(JSON.stringify({ dataExchangeProfileName: '' }))).toBeNull();
  });
});

describe('inferAttributesFromSample', () => {
  it('infers CSV columns with types from the header + first rows', () => {
    const attrs = inferAttributesFromSample('OrderNumber,CustomerId,Quantity\nA-1,9,3\nB-2,10,7');
    expect(attrs.map((a) => a.attributeName)).toEqual(['OrderNumber', 'CustomerId', 'Quantity']);
    expect(attrs[0].dataType).toBe(0); // String (A-1 is not numeric)
    expect(attrs[1].dataType).toBe(2); // Number
    expect(attrs[2].dataType).toBe(2); // Number
  });

  it('infers attributes from a JSON array sample', () => {
    const attrs = inferAttributesFromSample('[{"id": 1, "name": "x"}, {"id": 2, "name": "y"}]');
    expect(attrs.map((a) => [a.attributeName, a.dataType])).toEqual([
      ['id', 2],
      ['name', 0],
    ]);
  });

  it('returns [] for empty input', () => {
    expect(inferAttributesFromSample('   ')).toEqual([]);
  });
});
