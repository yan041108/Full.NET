import {
  auditingListHostAccessLogs,
  auditingListHostAccessLogsByCursor,
  isAuditingAccessLogCursorPage,
  isAuditingAccessLogPage,
  type AuditingAccessLog,
  type AuditingAccessLogCursorPage,
  type AuditingAccessLogPage,
  type AuditingAccessLogQuery
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询访问日志列表，并对响应页做失败关闭校验。 */
export async function listAuditingAccessLogs(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<AuditingAccessLogPage> {
  const value = await auditingListHostAccessLogs(
    http,
    { page, pageSize },
    signal
  );
  if (!isAuditingAccessLogPage(value)) {
    throw new Error('client.invalid_auditing_access_log_page');
  }

  return value;
}

/** 游标分页请求模型，兼容时间窗口与路径筛选。 */
export interface AuditingAccessLogCursorRequest
  extends AuditingAccessLogQuery {
  cursor?: string | null;
  limit?: number;
}

/** 按游标查询访问日志，适合长列表增量翻页与时间范围检索。 */
export async function listAuditingAccessLogsByCursor(
  options: AuditingAccessLogCursorRequest = {},
  signal?: AbortSignal
): Promise<AuditingAccessLogCursorPage> {
  const value = await auditingListHostAccessLogsByCursor(
    http,
    {
      limit: options.limit ?? 20,
      cursor: options.cursor ?? undefined,
      fromUtc: options.fromUtc,
      toUtc: options.toUtc,
      pathContains: options.pathContains
    },
    signal
  );
  if (!isAuditingAccessLogCursorPage(value)) {
    throw new Error('client.invalid_auditing_access_log_cursor_page');
  }

  return value;
}

/** 导出访问日志明细与分页模型，供日志页在列表与增量翻页间复用同一契约。 */
export type {
  AuditingAccessLog,
  AuditingAccessLogCursorPage,
  AuditingAccessLogPage
};
