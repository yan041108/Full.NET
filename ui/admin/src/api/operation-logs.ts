import {
  auditingListHostOperationLogs,
  isAuditingOperationLogPage,
  type AuditingOperationLog,
  type AuditingOperationLogPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询操作日志列表，并对响应页做失败关闭校验。 */
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

/** 导出操作日志明细与分页模型，供审计列表与筛选面板复用同一结果结构。 */
export type { AuditingOperationLog, AuditingOperationLogPage };
