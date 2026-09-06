import {
  isElasticsearchLogPipelineHealth,
  type ElasticsearchLogPipelineHealth
} from '@fullnet/client-contracts';
import { request } from './http';

/** 读取 Elasticsearch 日志管道健康状态。 */
export async function getElasticsearchLogPipelineHealth(
  signal?: AbortSignal
): Promise<ElasticsearchLogPipelineHealth> {
  const value = await request<unknown>(
    '/api/v1/observability/elasticsearch-log-pipeline/health',
    { method: 'GET' },
    signal
  );
  if (!isElasticsearchLogPipelineHealth(value)) {
    throw new Error('client.invalid_elasticsearch_log_pipeline_health');
  }

  return value;
}

export type { ElasticsearchLogPipelineHealth };
