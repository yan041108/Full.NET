import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { getElasticsearchLogPipelineHealth } from './observability-elasticsearch-health';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));

const requestMock = vi.mocked(request);

const health = {
  deliveryStatus: 'external-collector',
  deliveryConfirmationBoundary: 'configuration-only',
  elasticsearchQueryEnabled: false,
  elasticsearchEndpoint: null,
  elasticsearchIndexPattern: null,
  openTelemetryOtlpEndpointConfigured: false
};

describe('observability-elasticsearch-health api', () => {
  beforeEach(() => requestMock.mockReset());

  it('reads elasticsearch log pipeline health', async () => {
    requestMock.mockResolvedValueOnce(health);
    await getElasticsearchLogPipelineHealth();
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/observability/elasticsearch-log-pipeline/health',
      { method: 'GET' },
      undefined
    );
  });
});
