import {
  isAuditingOperationLogPage,
  type AuditingOperationLog,
  type AuditingOperationLogPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAuditingOperationLogs(
  page = 1,
  pageSize = 20
): Promise<AuditingOperationLogPage> {
  const value = await request<unknown>(
    `/api/v1/auditing/operation-logs?page=${page}&pageSize=${pageSize}`
  );
  if (!isAuditingOperationLogPage(value)) {
    throw new Error('client.invalid_auditing_operation_log_page');
  }
  return value;
}

export type { AuditingOperationLog, AuditingOperationLogPage };
