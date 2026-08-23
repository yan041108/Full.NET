import {
  documentHostGetDocumentStatistics,
  isHostDocumentStatisticsResponse,
  type HostDocumentStatisticsResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getDocumentStatistics(
  signal?: AbortSignal
): Promise<HostDocumentStatisticsResponse> {
  const value = await documentHostGetDocumentStatistics(http, {}, signal);
  if (!isHostDocumentStatisticsResponse(value)) {
    throw new Error('client.invalid_document_statistics');
  }
  return value;
}

export type { HostDocumentStatisticsResponse };
