import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PipelineVisualizer } from '@components/DataExchange/PipelineVisualizer';

// Numeric enums — the wire format after the Newtonsoft camelCase contract fix.
const numericProfile = JSON.stringify({
  dataExchangeProfileName: 'Customer Orders Import',
  dataSource: {
    dataSourceName: 'cust_orders.csv',
    mediumType: 3,
    mediumConfigurationJson: '{"filePath": "C:/temp/dx-e2e/cust_orders.csv"}',
  },
  pipeline: {
    // Deliberately out of order — the visualizer must sort by executionOrder.
    pipelineStages: [
      {
        stageType: 1,
        executionOrder: 2,
        pipelineStageActions: [
          {
            executionOrder: 1,
            action: {
              actionName: 'DispatchAll',
              type: 3,
              endpoint: { actionEndpointURL: 'file://out/orders.csv' },
              parameters: { OutputFormat: 'csv' },
            },
          },
        ],
      },
      {
        stageType: 0,
        executionOrder: 1,
        pipelineStageActions: [
          {
            executionOrder: 1,
            action: {
              actionName: 'MapToInternalSchema',
              type: 1,
              schemaMap: {
                attributeMappings: [
                  { targetAttribute: { attributeName: 'OrderNumber' }, transformType: 0 },
                  { targetAttribute: { attributeName: 'CustomerRef' }, transformType: 5 },
                  { targetAttribute: { attributeName: 'RegionName' }, transformType: 0 },
                ],
              },
            },
          },
        ],
      },
    ],
  },
});

// String enums — hand-edited profile docs (e.g. stock-store-page.json).
const stringProfile = JSON.stringify({
  dataExchangeProfileName: 'Stock File To Store Page',
  dataSource: {
    dataSourceName: 'live-stock.csv',
    mediumType: 'File',
    mediumConfigurationJson: '{"filePath": "C:/temp/live-stock.csv"}',
  },
  pipeline: {
    pipelineStages: [
      {
        stageType: 'DataTreatment',
        executionOrder: 1,
        pipelineStageActions: [
          {
            executionOrder: 1,
            action: {
              actionName: 'EnrichWebBreadCrum',
              type: 'EnrichmentLookup',
              lookup: {
                lookupEndpoint: 'http://localhost:5095/api/fake/lookup?sku={Sku}',
                type: 'Api',
                valueFieldToReturn: 'breadcrumb',
              },
            },
          },
        ],
      },
    ],
  },
});

describe('PipelineVisualizer', () => {
  it('renders source, stages in execution order and action details from a numeric-enum profile', () => {
    const { container } = render(<PipelineVisualizer jsonText={numericProfile} />);

    // Source card: medium label + file name + parsed filePath.
    expect(screen.getByText('File')).toBeTruthy();
    expect(screen.getByText('cust_orders.csv')).toBeTruthy();
    expect(screen.getByText(/C:\/temp\/dx-e2e\/cust_orders\.csv/)).toBeTruthy();

    // Stages sorted by executionOrder despite array order (PreRouting listed first).
    const text = container.textContent ?? '';
    expect(text.indexOf('DataTreatment')).toBeLessThan(text.indexOf('PreRouting'));

    // Transformation chip: mapping count + target attributes with transform labels.
    expect(screen.getByText('MapToInternalSchema')).toBeTruthy();
    expect(screen.getByText('3 mappings')).toBeTruthy();
    expect(screen.getByText('OrderNumber')).toBeTruthy();
    expect(screen.getAllByText(/DirectCopy/).length).toBe(2);
    expect(screen.getByText(/ToUpper/)).toBeTruthy();

    // Dispatch chip: endpoint URL + output format.
    expect(screen.getByText('file://out/orders.csv')).toBeTruthy();
    expect(screen.getByText('csv')).toBeTruthy();
  });

  it('accepts string enum values from hand-edited docs', () => {
    render(<PipelineVisualizer jsonText={stringProfile} />);
    expect(screen.getByText('DataTreatment')).toBeTruthy();
    expect(screen.getByText('EnrichWebBreadCrum')).toBeTruthy();
    expect(screen.getByText(/localhost:5095\/api\/fake\/lookup/)).toBeTruthy();
    expect(screen.getByText('Api')).toBeTruthy();
    expect(screen.getByText(/breadcrumb/)).toBeTruthy();
  });

  it('shows a hint instead of crashing on invalid JSON', () => {
    render(<PipelineVisualizer jsonText='{ not valid json' />);
    expect(screen.getByText(/Invalid JSON/)).toBeTruthy();
  });

  it('shows an empty-pipeline hint when no stages are defined', () => {
    render(
      <PipelineVisualizer
        jsonText={JSON.stringify({ dataExchangeProfileName: 'x', dataSource: { mediumType: 3 } })}
      />
    );
    expect(screen.getByText('No stages defined yet')).toBeTruthy();
  });

  it('shows a hint when the document has neither source nor pipeline', () => {
    render(<PipelineVisualizer jsonText='{"dataExchangeProfileName":"x"}' />);
    expect(screen.getByText(/No pipeline defined/)).toBeTruthy();
  });
});
