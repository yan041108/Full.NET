import type { WorkflowCcReadResponse, WorkflowCcResponse } from '@fullnet/client-contracts';
import { workflowFormIdValid, workflowFormValueKey } from './workflow-form-guards';
/** 保留 .NET 的七位秒小数，避免 Date.parse 截断到毫秒后误判首次时间。 */
export function workflowCcTimestampTicks(value: unknown): bigint | undefined {
  if (typeof value !== 'string') return undefined;
  const match = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.(\d{1,7}))?(?:Z|[+-]\d{2}:\d{2})$/u.exec(value), milliseconds = Date.parse(value);
  if (!match || !Number.isSafeInteger(milliseconds)) return undefined;
  return BigInt(milliseconds) * 10000n + BigInt((match[1] ?? '').padEnd(7, '0').slice(3));
}
const timestamp = (value: unknown): value is string => workflowCcTimestampTicks(value) !== undefined;
/** 仅供首次回执接入数据库六位秒小数；MySQL 可截断，SQL Server 按微秒舍入。 */
export function workflowCcPersistedReadMatches(receipt: string, stored: string): boolean {
  const source = workflowCcTimestampTicks(receipt), target = workflowCcTimestampTicks(stored); if (source === undefined || target === undefined) return false;
  const remainder = (source % 10n + 10n) % 10n, truncated = source - remainder;
  return target === truncated || target === truncated + (remainder >= 5n ? 10n : 0n);
}
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
/** 只接入可确认身份与状态的抄送，业务标识仍为服务端定义的普通字符串。 */
export function validWorkflowCc(value: unknown): value is WorkflowCcResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  return workflowFormIdValid(row.id) && workflowFormIdValid(row.instanceId) && (row.stepId === null || workflowFormIdValid(row.stepId))
    && text(row.nodeKey) && text(row.businessType) && text(row.businessId) && (row.businessTitle === null || typeof row.businessTitle === 'string')
    && timestamp(row.createdAtUtc) && (row.readAtUtc === null || timestamp(row.readAtUtc));
}
export function validWorkflowCcList(value: unknown): value is WorkflowCcResponse[] {
  return Array.isArray(value) && value.length <= 100 && value.every(validWorkflowCc) && new Set(value.map(row => row.id.toLowerCase())).size === value.length;
}
export function validWorkflowCcReceipt(value: unknown, id: string): value is WorkflowCcReadResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  return workflowFormIdValid(row.id) && row.id.toLowerCase() === id.toLowerCase() && timestamp(row.readAtUtc);
}
/** 已读时间仅允许从空推进到首次时间，资源绑定与启动时业务标题快照保持不变。 */
export const workflowCcBindingKey = (row: WorkflowCcResponse): string => workflowFormValueKey([row.id.toLowerCase(), row.instanceId.toLowerCase(), row.stepId?.toLowerCase() ?? null, row.nodeKey, row.businessType, row.businessId, row.businessTitle, workflowCcTimestampTicks(row.createdAtUtc)?.toString()]);
