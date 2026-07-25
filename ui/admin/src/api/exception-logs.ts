import {
  isAuditingExceptionLogPage,
  type AuditingExceptionLog,
  type AuditingExceptionLogPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAuditingExceptionLogs(
  page = 1,
  pageSize = 20
): Promise<AuditingExceptionLogPage> {
  const value = await request<unknown>(
    `/api/v1/auditing/exception-logs?page=${page}&pageSize=${pageSize}`
  );
  if (!isAuditingExceptionLogPage(value)) {
    throw new Error('client.invalid_auditing_exception_log_page');
  }
  return value;
}

export type { AuditingExceptionLog, AuditingExceptionLogPage };
