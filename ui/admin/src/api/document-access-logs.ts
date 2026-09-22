import {
  documentHostListDocumentAccessLogs,
  isHostDocumentAccessLogPage,
  type HostDocumentAccessLogPage,
  type HostDocumentAccessLogResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询文档访问日志。 */
export interface DocumentAccessLogListFilters {
  readonly documentItemId?: string;
  readonly accessTypeKey?: string;
  readonly sourceKey?: string;
}

export async function listDocumentAccessLogs(
  page = 1,
  pageSize = 20,
  filters: DocumentAccessLogListFilters = {},
  signal?: AbortSignal
): Promise<HostDocumentAccessLogPage> {
  const value = await documentHostListDocumentAccessLogs(
    http,
    {
      page,
      pageSize,
      documentItemId: filters.documentItemId,
      accessTypeKey: filters.accessTypeKey,
      sourceKey: filters.sourceKey
    },
    signal
  );
  if (!isHostDocumentAccessLogPage(value)) {
    throw new Error('client.invalid_document_access_log_page');
  }
  return value;
}

export type { HostDocumentAccessLogPage, HostDocumentAccessLogResponse };
