import { describe, expect, it } from 'vitest';
import { isElasticsearchLogPipelineHealth } from '../src/observability-elasticsearch-log-pipeline';

const legacyResponse = {
  adapterKind: 'serilog-elasticsearch',
  isEnabled: false,
  isSinkRegistered: false,
  indexFormat: 'fullnet-logs-{0:yyyy.MM.dd}',
  nodeEndpoints: [],
  openTelemetryOtlpEndpointConfigured: false,
  pipelineNotice: 'legacy',
  clusterStatus: 'disabled',
  clusterName: null,
  numberOfNodes: null,
  probeErrorMessage: null
};

describe('Elasticsearch log pipeline health contract', () => {
  it('accepts the old response during a rolling upgrade', () => {
    expect(isElasticsearchLogPipelineHealth(legacyResponse)).toBe(true);
  });

  it('requires a recognized delivery status and confirmation boundary together', () => {
    expect(isElasticsearchLogPipelineHealth({
      ...legacyResponse,
      deliveryStatus: 'external-collector',
      deliveryConfirmationBoundary: 'configuration-only'
    })).toBe(true);
    expect(isElasticsearchLogPipelineHealth({
      ...legacyResponse,
      deliveryStatus: 'external-collector'
    })).toBe(false);
    expect(isElasticsearchLogPipelineHealth({
      ...legacyResponse,
      deliveryStatus: 'delivered',
      deliveryConfirmationBoundary: 'configuration-only'
    })).toBe(false);
  });
});
