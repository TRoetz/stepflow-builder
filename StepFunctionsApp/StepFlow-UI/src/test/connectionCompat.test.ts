import { describe, it, expect } from 'vitest';
import {
  areTypesCompatible,
  findFirstCompatiblePair,
  isConnectable,
  firstCompatibleInputLabel,
} from '@utils/connectionCompat';
import type { StepInput, StepOutput } from '@schema-types/schema';

const inPort = (id: string, type: StepInput['type'], label?: string): StepInput => ({
  id,
  label: label ?? `in_${id}`,
  type,
  optional: false,
  position: 'left',
});

const outPort = (id: string, type: StepOutput['type']): StepOutput => ({
  id,
  label: `out_${id}`,
  type,
  position: 'right',
});

describe('areTypesCompatible', () => {
  it('matches identical types', () => {
    expect(areTypesCompatible('json', 'json')).toBe(true);
    expect(areTypesCompatible('string', 'string')).toBe(true);
  });

  it('wildcards anything via "any"', () => {
    expect(areTypesCompatible('any', 'image')).toBe(true);
    expect(areTypesCompatible('number', 'any')).toBe(true);
    expect(areTypesCompatible('any', 'any')).toBe(true);
  });

  it('rejects mismatched concrete types', () => {
    expect(areTypesCompatible('json', 'string')).toBe(false);
    expect(areTypesCompatible('image', 'number')).toBe(false);
  });
});

describe('findFirstCompatiblePair', () => {
  const outputs = [outPort('o1', 'json'), outPort('o2', 'string')];
  const inputs = [inPort('i1', 'string'), inPort('i2', 'any')];

  it('finds the first compatible output→input pair', () => {
    // o1(json) vs i1(string): no. o1(json) vs i2(any): yes.
    expect(findFirstCompatiblePair(outputs, inputs)).toEqual({
      sourceHandle: 'o1',
      targetHandle: 'i2',
    });
  });

  it('prefers earlier outputs even when a later one matches more specifically', () => {
    const out = [outPort('a', 'string'), outPort('b', 'json')];
    const inp = [inPort('x', 'any'), inPort('y', 'json')];
    expect(findFirstCompatiblePair(out, inp)).toEqual({ sourceHandle: 'a', targetHandle: 'x' });
  });

  it('returns null when nothing is compatible', () => {
    expect(findFirstCompatiblePair([outPort('o', 'image')], [inPort('i', 'string')])).toBeNull();
  });
});

describe('isConnectable', () => {
  it('false for empty outputs (e.g. End node)', () => {
    expect(isConnectable([], [inPort('i', 'any')])).toBe(false);
  });

  it('false for empty inputs on candidate', () => {
    expect(isConnectable([outPort('o', 'json')], [])).toBe(false);
  });

  it('true when a compatible pair exists', () => {
    expect(isConnectable([outPort('o', 'json')], [inPort('i', 'any')])).toBe(true);
  });
});

describe('firstCompatibleInputLabel', () => {
  it('returns the label of the first matching input', () => {
    const outputs = [outPort('o1', 'json')];
    const inputs = [inPort('i1', 'string', 'Wrong type'), inPort('i2', 'any', 'Payload')];
    expect(firstCompatibleInputLabel(outputs, inputs)).toBe('Payload');
  });

  it('returns null when nothing matches', () => {
    expect(firstCompatibleInputLabel([outPort('o1', 'image')], [inPort('i1', 'number')])).toBeNull();
  });
});
