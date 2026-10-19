import { describe, expect, it } from 'vitest';
import { isElasticsearchLogPipelineHealth } from '../src/observability-elasticsearch-log-pipeline';
const response = {
  deliveryStatus: 'external-collector',
  deliveryConfirmationBoundary: 'configuration-only',
  elasticsearchQueryEnabled: false,
  elasticsearchEndpoint: null,
  elasticsearchIndexPattern: null,
  openTelemetryOtlpEndpointConfigured: false
};
describe('日志入口与 ES 查询契约', () => {
  it('接收三种入口并要求明确的配置确认边界', () => {
    for (const deliveryStatus of ['local', 'external-collector', 'application-kafka']) {
      expect(isElasticsearchLogPipelineHealth({ ...response, deliveryStatus })).toBe(true);
    }
    expect(isElasticsearchLogPipelineHealth({ ...response, deliveryConfirmationBoundary: undefined })).toBe(false);
  });
  it('拒绝旧入口和未经证明的投递成功', () => {
    for (const deliveryStatus of ['legacy-console', 'legacy-direct', 'delivered']) {
      expect(isElasticsearchLogPipelineHealth({ ...response, deliveryStatus })).toBe(false);
    }
    expect(isElasticsearchLogPipelineHealth({ ...response, deliveryConfirmationBoundary: 'sink-registered-only' })).toBe(false);
  });
});
