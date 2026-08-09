import {
  isHostDocumentStatisticsResponse,
  type HostDocumentStatisticsResponse
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getDocumentStatistics(): Promise<HostDocumentStatisticsResponse> {
  const value = await request<unknown>('/api/v1/document/host/statistics');
  if (!isHostDocumentStatisticsResponse(value)) {
    throw new Error('client.invalid_document_statistics');
  }
  return value;
}
