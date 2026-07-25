import {
  isAuditingAccessLogPage,
  type AuditingAccessLog,
  type AuditingAccessLogPage
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

export type { AuditingAccessLog, AuditingAccessLogPage };
