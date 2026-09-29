import {
  auditingGetHostOperationLogDetails,
  auditingListHostOperationLogs,
  isAuditingOperationLogPage,
  type AuditingOperationLog,
  type AuditingOperationLogPage,
  type AuditingListHostOperationLogsParameters,
  type OperationLogDetailsResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export type AuditingOperationLogFilters = Pick<
  AuditingListHostOperationLogsParameters,
  'fromUtc' | 'toUtc' | 'httpMethod' | 'succeeded' | 'pathContains'
>;

/** 分页查询操作日志列表，并对响应页做失败关闭校验。 */
export async function listAuditingOperationLogs(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal,
  filters: AuditingOperationLogFilters = {}
): Promise<AuditingOperationLogPage> {
  const value = await auditingListHostOperationLogs(
    http,
    { page, pageSize, ...filters },
    signal
  );
  if (!isAuditingOperationLogPage(value)) {
    throw new Error('client.invalid_auditing_operation_log_page');
  }

  return value;
}

/** 仅在详情权限门通过后按需读取受限操作详情；响应由生成契约校验。 */
export async function getAuditingOperationLogDetails(
  operationLogId: string,
  signal?: AbortSignal
): Promise<OperationLogDetailsResponse> {
  const details = await auditingGetHostOperationLogDetails(http, { operationLogId }, signal);
  // 受限响应必须与当前所选日志一致，避免错误映射或缓存结果显示另一条记录的详情。
  if (details.id.toLowerCase() !== operationLogId.toLowerCase()) {
    throw new Error('client.invalid_auditing_operation_log_details_id');
  }
  return details;
}

/** 导出操作日志明细与分页模型，供审计列表与筛选面板复用同一结果结构。 */
export type { AuditingOperationLog, AuditingOperationLogPage, OperationLogDetailsResponse };
