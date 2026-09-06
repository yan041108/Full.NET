import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { getElasticsearchLogPipelineHealth } from './observability-elasticsearch-health';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));

const requestMock = vi.mocked(request);

const health = {
  adapterKind: 'serilog-elasticsearch',
  isEnabled: false,
  isSinkRegistered: false,
  indexFormat: 'fullnet-logs-{0:yyyy.MM.dd}',
  nodeEndpoints: [],
  openTelemetryOtlpEndpointConfigured: false,
  pipelineNotice: 'pipeline notice',
  clusterStatus: 'disabled',
  clusterName: null,
  numberOfNodes: null,
  probeErrorMessage: null
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
