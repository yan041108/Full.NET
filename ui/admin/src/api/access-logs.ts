import {
  isAuditingAccessLogCursorPage,
  isAuditingAccessLogPage,
  type AuditingAccessLog,
  type AuditingAccessLogCursorPage,
  type AuditingAccessLogPage,
  type AuditingAccessLogQuery
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAuditingAccessLogs(
  page = 1,
  pageSize = 20
): Promise<AuditingAccessLogPage> {
  const value = await request<unknown>(
    `/api/v1/auditing/access-logs?page=${page}&pageSize=${pageSize}`
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
  options: AuditingAccessLogCursorRequest = {}
): Promise<AuditingAccessLogCursorPage> {
  const parameters = new URLSearchParams();
  parameters.set('limit', String(options.limit ?? 20));
  if (options.fromUtc) {
    parameters.set('fromUtc', options.fromUtc);
  }
  if (options.toUtc) {
    parameters.set('toUtc', options.toUtc);
  }
  if (options.pathContains) {
    parameters.set('pathContains', options.pathContains);
  }
  if (options.cursor) {
    parameters.set('cursor', options.cursor);
  }
  const value = await request<unknown>(
    `/api/v1/auditing/access-logs/cursor?${parameters.toString()}`
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
