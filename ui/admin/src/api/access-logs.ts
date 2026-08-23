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

export interface AuditingAccessLogCursorRequest
  extends AuditingAccessLogQuery {
  cursor?: string | null;
  limit?: number;
}

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

export type {
  AuditingAccessLog,
  AuditingAccessLogCursorPage,
  AuditingAccessLogPage
};
