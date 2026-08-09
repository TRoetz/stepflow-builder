import { describe, it, expect } from 'vitest';
import { getCategoryColor, getDataTypeColor, hexToRgba, adjustColor } from '@utils/colors';
import { calculateNextPosition, snapToGrid, calculateBoundingBox } from '@utils/layout';
import { generateId, generateNodeId, generateEdgeId } from '@utils/id';

describe('Color Utilities', () => {
  it('should return valid hex colors for categories', () => {
    const categories = ['ai', 'rule', 'data', 'api', 'transform', 'utility', 'subflow'];
    for (const cat of categories) {
      const color = getCategoryColor(cat as Parameters<typeof getCategoryColor>[0]);
      expect(color).toMatch(/^#[0-9A-Fa-f]{6}$/);
    }
  });

  it('should return valid hex colors for data types', () => {
    const types = ['json', 'string', 'number', 'boolean', 'array', 'image', 'any'];
    for (const type of types) {
      const color = getDataTypeColor(type as Parameters<typeof getDataTypeColor>[0]);
      expect(color).toMatch(/^#[0-9A-Fa-f]{6}$/);
    }
  });

  it('should convert hex to rgba', () => {
    const rgba = hexToRgba('#ff0000', 0.5);
    expect(rgba).toBe('rgba(255, 0, 0, 0.5)');
  });

  it('should adjust color brightness', () => {
    const darker = adjustColor('#000000', -50);
    const lighter = adjustColor('#ffffff', 50);
    expect(darker).toMatch(/^#[0-9A-Fa-f]{6}$/);
    expect(lighter).toMatch(/^#[0-9A-Fa-f]{6}$/);
  });
});

describe('Layout Utilities', () => {
  it('should return default position for empty nodes', () => {
    const pos = calculateNextPosition([]);
    expect(pos).toEqual({ x: 250, y: 150 });
  });

  it('should calculate next position from existing nodes', () => {
    const nodes = [{ position: { x: 100, y: 100 } }];
    const pos = calculateNextPosition(nodes);
    expect(pos.x).toBeGreaterThan(0);
    expect(pos.y).toBeGreaterThan(100);
  });

  it('should snap to grid', () => {
    const snapped = snapToGrid({ x: 105, y: 207 });
    expect(snapped).toEqual({ x: 112, y: 208 });
  });

  it('should calculate bounding box', () => {
    const nodes = [
      { position: { x: 0, y: 0 }, measured: { width: 100, height: 50 } },
      { position: { x: 200, y: 100 }, measured: { width: 100, height: 50 } },
    ];
    const box = calculateBoundingBox(nodes);
    expect(box.x).toBe(0);
    expect(box.y).toBe(0);
    expect(box.width).toBe(300);
    expect(box.height).toBe(150);
  });
});

describe('ID Utilities', () => {
  it('should generate unique IDs', () => {
    const id1 = generateId();
    const id2 = generateId();
    expect(id1).not.toBe(id2);
    expect(id1).toBeTruthy();
  });

  it('should generate prefixed IDs', () => {
    const id = generateId('node');
    expect(id).toMatch(/^node-/);
  });

  it('should generate node IDs', () => {
    const id = generateNodeId('stepflow:utility:pass');
    expect(id).toMatch(/^node-stepflow-utility-pass-/);
  });

  it('should generate edge IDs', () => {
    const id = generateEdgeId('node-1', 'node-2');
    expect(id).toMatch(/^edge-/);
  });
});
