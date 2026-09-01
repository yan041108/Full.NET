import {
  documentHostGetDocumentStatistics,
  isHostDocumentStatisticsResponse,
  type HostDocumentStatisticsResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 读取 Host 文档统计摘要，并对响应结构做失败关闭校验。 */
export async function getDocumentStatistics(
  signal?: AbortSignal
): Promise<HostDocumentStatisticsResponse> {
  const value = await documentHostGetDocumentStatistics(http, {}, signal);
  if (!isHostDocumentStatisticsResponse(value)) {
    throw new Error('client.invalid_document_statistics');
  }
  return value;
}

/** 导出文档统计聚合模型，供仪表卡片与统计图表复用同一摘要结构。 */
export type { HostDocumentStatisticsResponse };
