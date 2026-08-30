import { describe, it, expect } from 'vitest';
import { autoMapSameNames, type AttributeMapping, type EntityAttribute } from '@components/DataExchange/SchemaEditor';

describe('autoMapSameNames', () => {
  const inbound: EntityAttribute[] = [
    { entityAttributeId: 1, attributeName: 'OrderNumber', dataType: 0 },
    { entityAttributeId: 2, attributeName: 'CustomerId', dataType: 2 },
    { entityAttributeId: 3, attributeName: 'Quantity', dataType: 2 },
  ];

  it('creates a DirectCopy mapping for each unmapped inbound attribute', () => {
    const result = autoMapSameNames([], inbound);
    expect(result).toHaveLength(3);
    expect(result[0]).toMatchObject({ transformType: 0, targetAttribute: { attributeName: 'OrderNumber', dataType: 0 } });
    expect(result[0].sourceAttributes).toEqual([{ entityAttributeId: 1, attributeName: 'OrderNumber', dataType: 0 }]);
  });

  it('is idempotent — a second run changes nothing', () => {
    const once = autoMapSameNames([], inbound);
    const twice = autoMapSameNames(once, inbound);
    expect(twice).toEqual(once);
  });

  it('extends an existing mapping with missing same-named sources instead of duplicating', () => {
    const existing: AttributeMapping[] = [
      { transformType: 0, targetAttribute: { attributeName: 'OrderNumber' }, sourceAttributes: [{ entityAttributeId: 9, attributeName: 'Other' }] },
    ];
    const result = autoMapSameNames(existing, inbound);
    expect(result).toHaveLength(3); // OrderNumber extended in place + CustomerId + Quantity added
    const orderMapping = result.find((m) => m.targetAttribute?.attributeName === 'OrderNumber');
    expect(orderMapping?.sourceAttributes?.map((s) => s.attributeName)).toEqual(['Other', 'OrderNumber']);
  });

  it('matches existing mappings by target id as well as name', () => {
    const existing: AttributeMapping[] = [{ transformType: 0, targetAttributeId: 2, sourceAttributes: [] }];
    const result = autoMapSameNames(existing, inbound);
    expect(result).toHaveLength(3); // no duplicate for CustomerId (id match)
    expect(result.find((m) => m.targetAttributeId === 2)?.sourceAttributes?.map((s) => s.attributeName)).toEqual(['CustomerId']);
  });

  it('skips attributes without a name', () => {
    const result = autoMapSameNames([], [{ entityAttributeId: 5, dataType: 0 }]);
    expect(result).toHaveLength(0);
  });
});
