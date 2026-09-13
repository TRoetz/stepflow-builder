// Contract tests for the Data Exchange profile wizard model. The wire document
// the builder emits is what the .NET executor deserializes (StepFunctionsApp/
// DataExchange): attributeMappings use target/source objects with attributeName,
// enums are ordinals, dispatch parameters use PascalCase keys, enrichment URLs
// carry raw {Field} tokens. These tests pin that contract.

import { describe, expect, it } from 'vitest';

import {
  autoMatchSources,
  buildWireDocument,
  domainRegistryEntry,
  emptyWizardState,
  profileToWizardState,
  slugify,
  syncMappingsWithTarget,
  validateWizardState,
  type WizardState,
} from '@components/DataExchange/profileWizardModel';

function sampleState(): WizardState {
  const s = emptyWizardState();
  s.name = 'Stock To Store';
  s.filePath = 'input/orders.csv';
  s.importSchema = {
    mode: 'defined',
    name: 'OrderImport',
    fields: [
      { name: 'COMPANY_NAME', dataType: 0 },
      { name: 'PRICE', dataType: 2 },
      { name: 'QTY', dataType: 2 },
      { name: 'ORDER_ID', dataType: 0 },
    ],
    register: false,
  };
  s.targetSchema = {
    mode: 'defined',
    name: 'StoreOrder',
    register: false,
    fields: [
      { name: 'Name', dataType: 0 },
      { name: 'Total', dataType: 2 },
      { name: 'Notes', dataType: 0 },
    ],
  };
  s.mappings = [
    { target: 'Name', sources: ['COMPANY_NAME'], transform: 0, parameter: '' },
    { target: 'Total', sources: ['PRICE', 'QTY'], transform: 2, parameter: '' },
    { target: 'Notes', sources: [], transform: 3, parameter: 'pending' },
  ];
  s.lookups = [
    {
      name: 'CompanyQuote',
      url: 'https://api.example.com/quote?symbols={COMPANY_NAME}',
      valueField: 'price',
      outputName: '',
      body: '',
    },
  ];
  s.dispatches = [
    { name: 'ToApi', url: 'https://api.internal/orders', filter: '{ORDER_ID} IS NOT NULL', method: '', format: '', batch: false },
    { name: 'ToFile', url: 'file://out/orders.csv', filter: '', method: '', format: 'csv', batch: true },
  ];
  return s;
}

describe('buildWireDocument', () => {
  it('emits the backend document contract for a full wizard state', () => {
    const doc = buildWireDocument(sampleState()) as Record<string, unknown>;

    expect(doc.dataExchangeProfileName).toBe('Stock To Store');
    expect(doc.profileId).toBe('stock-to-store');
    expect(doc.isActive).toBe(true);

    const dataSource = doc.dataSource as Record<string, unknown>;
    expect(dataSource.mediumType).toBe(3); // ordinal, File
    expect(JSON.parse(dataSource.mediumConfigurationJson as string)).toEqual({ filePath: 'input/orders.csv' });

    const importSchema = dataSource.importSchema as Record<string, unknown>;
    expect(importSchema.attributeDomainName).toBe('OrderImport');
    const attrs = importSchema.attributes as Array<Record<string, unknown>>;
    expect(attrs.map((a) => a.attributeName)).toEqual(['COMPANY_NAME', 'PRICE', 'QTY', 'ORDER_ID']);

    const stages = (doc.pipeline as Record<string, unknown>).pipelineStages as Array<Record<string, unknown>>;
    expect(stages.map((s) => s.stageType)).toEqual([0, 1]); // DataTreatment, then PreRouting

    const treatmentActions = (stages[0].pipelineStageActions as Array<Record<string, unknown>>).map((l) => l.action);
    const transformation = treatmentActions[0] as Record<string, unknown>;
    expect(transformation.type).toBe(1);
    const mappings = ((transformation.schemaMap as Record<string, unknown>).attributeMappings as Array<Record<string, unknown>>);
    expect(mappings[0]).toEqual({
      targetAttribute: { attributeName: 'Name' },
      sourceAttributes: [{ attributeName: 'COMPANY_NAME' }],
      transformType: 0,
      mergeStrategy: 1,
    });
    expect(mappings[1].sourceAttributes).toHaveLength(2);
    expect((mappings[2].parameters as Record<string, unknown>).defaultValue).toBe('pending');

    const lookup = treatmentActions[1] as Record<string, unknown>;
    expect(lookup.type).toBe(2);
    const lookupJson = lookup.lookup as Record<string, unknown>;
    expect(lookupJson.lookupEndpoint).toBe('https://api.example.com/quote?symbols={COMPANY_NAME}');
    expect(lookupJson.valueFieldToReturn).toBe('price');
    expect(lookup.type).toBe(2);

    const dispatchLinks = (stages[1].pipelineStageActions as Array<Record<string, unknown>>);
    const api = dispatchLinks[0].action as Record<string, unknown>;
    expect((api.endpoint as Record<string, unknown>).actionEndpointURL).toBe('https://api.internal/orders');
    expect((api.parameters as Record<string, unknown>).Filter).toBe('{ORDER_ID} IS NOT NULL');
    const file = dispatchLinks[1].action as Record<string, unknown>;
    expect((file.parameters as Record<string, unknown>).Method).toBeUndefined(); // POST is implicit
    expect((file.parameters as Record<string, unknown>).Batch).toBe('true');
    expect((file.parameters as Record<string, unknown>).OutputFormat).toBe('csv');
  });

  it('skips the PreRouting stage when there are no dispatches', () => {
    const s = sampleState();
    s.dispatches = [];
    const stages = ((buildWireDocument(s) as Record<string, unknown>).pipeline as Record<string, unknown>).pipelineStages as unknown[];
    expect(stages).toHaveLength(1);
  });
});

const EXOTIC_DOC = {
  dataExchangeProfileName: 'Legacy Import',
  isActive: false,
  owner: 'ops-team', // unknown top-level key
  dataSource: {
    dataSourceName: 'orders',
    mediumType: 'File', // string enum variant (hand-written docs)
    mediumConfigurationJson: '{"filePath":"a.csv","delimiter":";"}',
    importSchema: {
      attributeDomainName: 'OrdersRaw',
      version: '1',
      attributes: [{ attributeName: 'A', dataType: 0 }],
    },
  },
  pipeline: {
    pipelineName: 'legacy',
    pipelineStages: [
      {
        stageType: 'DataTreatment',
        executionOrder: 1,
        pipelineStageActions: [
          {
            executionOrder: 1,
            action: {
              actionName: 'Transform',
              type: 'Transformation',
              schemaMap: {
                attributeMappings: [
                  {
                    targetAttribute: { attributeName: 'B' },
                    sourceAttributes: [{ attributeName: 'A' }],
                    transformType: 'Trim',
                  },
                ],
              },
            },
          },
          {
            executionOrder: 2,
            action: {
              actionName: 'Gate',
              type: 'Logic',
              parameters: { Expression: '{A} <> ""' },
            },
          },
        ],
      },
    ],
  },
};

describe('profileToWizardState', () => {
  it('reads string enums and preserves unknown actions and top-level keys', () => {
    const state = profileToWizardState(EXOTIC_DOC as never);

    expect(state.name).toBe('Legacy Import');
    expect(state.isActive).toBe(false);
    expect(state.extraTop.owner).toBe('ops-team');
    expect(state.extraMediumConfig).toEqual({ delimiter: ';' });
    expect(state.filePath).toBe('a.csv');
    expect(state.importSchema.name).toBe('OrdersRaw');
    expect(state.importSchema.fields).toEqual([{ name: 'A', dataType: 0 }]);

    // Trim = ordinal 4 regardless of the string form in the document
    expect(state.mappings).toHaveLength(1);
    expect(state.mappings[0].transform).toBe(4);
    expect(state.targetSchema.fields).toEqual([{ name: 'B', dataType: 0 }]);

    // The Logic action is not editable but survives
    expect(state.extraActions).toHaveLength(1);
    expect((state.extraActions[0] as Record<string, unknown>).actionName).toBe('Gate');
  });

  it('re-emits unknown content unchanged on the next save', () => {
    const back = buildWireDocument(profileToWizardState(EXOTIC_DOC as never)) as Record<string, unknown>;
    expect(back.owner).toBe('ops-team');
    expect(JSON.parse((back.dataSource as Record<string, unknown>).mediumConfigurationJson as string)).toEqual({
      filePath: 'a.csv',
      delimiter: ';',
    });
    const actions = ((back.pipeline as Record<string, unknown>).pipelineStages as Array<Record<string, unknown>>)
      .flatMap((s) => (s.pipelineStageActions as Array<Record<string, unknown>>).map((l) => l.action));
    expect(actions.some((a) => (a as Record<string, unknown>).actionName === 'Gate')).toBe(true);
  });

  it('round-trips a wizard-built document without losing mappings or routing', () => {
    const first = buildWireDocument(sampleState());
    const loaded = profileToWizardState(first);
    expect(loaded.mappings).toEqual(sampleState().mappings);
    expect(loaded.lookups).toEqual(sampleState().lookups);
    expect(loaded.dispatches).toEqual(sampleState().dispatches);
    expect(loaded.importSchema.fields).toEqual(sampleState().importSchema.fields);
    expect(loaded.targetSchema.name).toBe('StoreOrder');
    const second = buildWireDocument(loaded);
    expect(second).toEqual(first);
  });
});

describe('mapping helpers', () => {
  it('syncMappingsWithTarget adds rows for new fields and drops deleted ones', () => {
    const existing = { target: 'B', sources: ['X'], transform: 0, parameter: '' };
    const result = syncMappingsWithTarget([existing, { target: 'gone', sources: [], transform: 0, parameter: '' }], [
      { name: 'B', dataType: 0 },
      { name: 'C', dataType: 2 },
    ]);
    expect(result.map((m) => m.target)).toEqual(['B', 'C']);
    expect(result[0].sources).toEqual(['X']);
    expect(result[1]).toEqual({ target: 'C', sources: [], transform: 0, parameter: '' });
  });

  it('autoMatchSources fills only rows without sources, case-insensitively', () => {
    const fields = [{ name: 'Order_Id', dataType: 0 }];
    const mappings = [
      { target: 'order_id', sources: [], transform: 0, parameter: '' },
      { target: 'Other', sources: ['Custom'], transform: 0, parameter: '' },
    ];
    const result = autoMatchSources(mappings, fields);
    expect(result[0].sources).toEqual(['Order_Id']);
    expect(result[1].sources).toEqual(['Custom']);
  });
});

describe('validateWizardState', () => {
  it('flags an empty wizard on the first steps only', () => {
    const issues = validateWizardState(emptyWizardState());
    const steps = new Set(issues.map((i) => i.step));
    expect(steps.has(0)).toBe(true);
    expect(steps.has(1)).toBe(true);
    expect(steps.has(4)).toBe(false);
    expect(issues.some((i) => i.anchor === 'name')).toBe(true);
  });

  it('rejects sources that are not in the import schema', () => {
    const s = sampleState();
    s.mappings[0].sources = ['NOPE'];
    const issues = validateWizardState(s);
    expect(issues.some((i) => i.step === 3 && /NOPE/.test(i.message))).toBe(true);
    // A direct-copy row missing sources is flagged; a Set fixed value row is not
    s.mappings[1].sources = [];
    expect(validateWizardState(s).some((i) => /Total/.test(i.message))).toBe(true);
  });

  it('accepts a complete state', () => {
    expect(validateWizardState(sampleState())).toHaveLength(0);
  });
});

describe('registry entry', () => {
  it('matches the attribute-domain save payload shape', () => {
    const entry = domainRegistryEntry(sampleState().importSchema, 'orders import') as Record<string, unknown>;
    expect(Object.keys(entry)).toEqual(['schemaDefinition', 'attributeDomain']);
    const domain = entry.attributeDomain as Record<string, unknown>;
    expect(domain.attributeDomainName).toBe('OrderImport');
    const attrs = domain.attributes as Array<Record<string, unknown>>;
    expect(attrs[0]).toMatchObject({ attributeName: 'COMPANY_NAME', displayName: 'COMPANY_NAME', visible: true });
  });
});

describe('slugify', () => {
  it('handles spaces, punctuation, and casing', () => {
    expect(slugify('  Stock To Store! ')).toBe('stock-to-store');
    expect(slugify('A--B  C')).toBe('a-b-c');
  });
});
