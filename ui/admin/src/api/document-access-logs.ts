import {
  documentHostListDocumentAccessLogs,
  isHostDocumentAccessLogPage,
  isGuid,
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
  const query = { page, pageSize, documentItemId: filters.documentItemId?.trim() || undefined, accessTypeKey: filters.accessTypeKey?.trim() || undefined, sourceKey: filters.sourceKey?.trim() || undefined };
  if (!Number.isSafeInteger(page) || page < 1 || page > 2147483647 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100
    || query.documentItemId && (!isGuid(query.documentItemId) || query.documentItemId === '00000000-0000-0000-0000-000000000000')) throw new Error('client.invalid_document_access_log_query');
  const value = await documentHostListDocumentAccessLogs(
    http,
    query,
    signal
  );
  // 生成客户端验证形状，此处复核冻结分页和过滤归属，不能把错页或错文档当作当前日志。
  if (!isHostDocumentAccessLogPage(value) || value.page !== page || value.pageSize !== pageSize || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > pageSize
    || new Set(value.items.map(item => item.id.toLowerCase())).size !== value.items.length
    || value.items.some(item => item.id === '00000000-0000-0000-0000-000000000000' || item.documentItemId === '00000000-0000-0000-0000-000000000000'
      || query.documentItemId && item.documentItemId.toLowerCase() !== query.documentItemId.toLowerCase() || query.accessTypeKey && item.accessTypeKey !== query.accessTypeKey || query.sourceKey && item.sourceKey !== query.sourceKey)) {
    throw new Error('client.invalid_document_access_log_page');
  }
  return value;
}

export type { HostDocumentAccessLogPage, HostDocumentAccessLogResponse };
