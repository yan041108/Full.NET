import {
  auditingListHostOperationLogs,
  isAuditingOperationLogPage,
  type AuditingOperationLog,
  type AuditingOperationLogPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listAuditingOperationLogs(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<AuditingOperationLogPage> {
  const value = await auditingListHostOperationLogs(
    http,
    { page, pageSize },
    signal
  );
  if (!isAuditingOperationLogPage(value)) {
    throw new Error('client.invalid_auditing_operation_log_page');
  }

  return value;
}

export type { AuditingOperationLog, AuditingOperationLogPage };
