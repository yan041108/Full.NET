import type { PagedResultOfWorkflowRecoveryTaskResponse, WorkflowRecoveryTaskResponse } from '@fullnet/client-contracts';
import { workflowFormIdValid, workflowFormRevisionValid } from './workflow-form-guards';
export const recoveryStatuses = ['pending', 'failed', 'dead_lettered', 'succeeded', 'cancelled'] as const;
export const recoveryTrim = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
const nullableText = (value: unknown): boolean => value === null || typeof value === 'string';
const countValid = (value: unknown): boolean => Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 2147483647;
/** 未知状态或不安全修订不能变成可操作任务；租约由服务端修订和代次共同保护。 */
export function validRecoveryTask(value: unknown): value is WorkflowRecoveryTaskResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const row = value as Record<string, unknown>;
  return workflowFormIdValid(row.id) && workflowFormIdValid(row.instanceId) && (row.stepId === null || workflowFormIdValid(row.stepId))
    && ['expired_lease', 'stuck_instance', 'incomplete_step'].includes(String(row.kindKey)) && recoveryStatuses.includes(row.statusKey as typeof recoveryStatuses[number])
    && workflowFormRevisionValid(row.revision) && countValid(row.attemptCount) && countValid(row.leaseGeneration)
    && nullableText(row.leaseOwnerKey) && nullableText(row.leaseExpiresAtUtc) && nullableText(row.nextAttemptAtUtc) && nullableText(row.lastError)
    && typeof row.createdAtUtc === 'string' && typeof row.updatedAtUtc === 'string';
}
/** COUNT 与分页读取并非同一快照，总数可以暂时低于本次行数。 */
export function validRecoveryPage(value: unknown, page: number, pageSize: number): value is PagedResultOfWorkflowRecoveryTaskResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const result = value as Record<string, unknown>;
  return result.page === page && result.pageSize === pageSize && Number.isSafeInteger(result.total) && Number(result.total) >= 0
    && Array.isArray(result.items) && result.items.length <= pageSize && result.items.every(validRecoveryTask)
    && new Set(result.items.map(row => row.id.toLowerCase())).size === result.items.length;
}
