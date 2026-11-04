import type { WorkflowExecutionLogResponse, WorkflowInstanceListItemResponse, WorkflowInstanceResponse } from '@fullnet/client-contracts';
import { workflowFormIdValid as idValid, workflowFormRevisionValid, workflowFormValueKey } from './workflow-form-guards';
export const instanceStatuses = ['active', 'completed', 'rejected', 'cancelled', 'suspended'] as const;
export const instanceTrim = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const nullableText = (value: unknown): boolean => value === null || typeof value === 'string';
const optionalText = (value: unknown): boolean => value === undefined || nullableText(value);
const count = (value: unknown): boolean => Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 2147483647;
const status = (value: unknown): boolean => instanceStatuses.includes(value as typeof instanceStatuses[number]);
function base(row: Record<string, unknown>): boolean { return idValid(row.id) && idValid(row.definitionVersionId) && text(row.businessType) && text(row.businessId) && nullableText(row.businessTitle) && status(row.statusKey) && text(row.startedAtUtc); }
/** 详情聚合超时/投票/网关事实，不把这些可独立变化的摘要当作实例修订的一部分。 */
export function validWorkflowInstance(value: unknown): value is WorkflowInstanceResponse {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  if (!base(row) || !idValid(row.formVersionId) || !workflowFormRevisionValid(row.revision) || row.activeTodoId !== null && !idValid(row.activeTodoId)) return false;
  if (!optionalText(row.dueAtUtc) || !optionalText(row.escalatedAtUtc) || !optionalText(row.activeNodeKey) || !optionalText(row.approvalModeKey) || row.timeoutStatusKey !== undefined && !['scheduled', 'overdue', 'escalated', 'not_configured'].includes(String(row.timeoutStatusKey))) return false;
  if (row.reminderCount !== undefined && !count(row.reminderCount)) return false;
  for (const key of ['requiredApprovalCount', 'approvedCount', 'rejectedCount', 'pendingCount']) if (row[key] !== undefined && row[key] !== null && !count(row[key])) return false;
  if (row.gatewayJoins == null) return true; if (!Array.isArray(row.gatewayJoins)) return false;
  const ids = new Set<string>(); return row.gatewayJoins.every((join: unknown) => { if (!join || typeof join !== 'object') return false; const item = join as Record<string, unknown>;
    if (!idValid(item.id) || ids.has(item.id.toLowerCase()) || !text(item.gatewayTypeKey) || !text(item.forkNodeKey) || !text(item.joinNodeKey) || !text(item.statusKey) || !count(item.requiredBranchCount) || !count(item.arrivedBranchCount) || !Array.isArray(item.branches)) return false; ids.add(item.id.toLowerCase());
    const keys = new Set<string>(); return item.branches.every((value: unknown) => { if (!value || typeof value !== 'object') return false; const branch = value as Record<string, unknown>; if (!text(branch.branchKey) || keys.has(branch.branchKey) || !optionalText(branch.arrivedAtUtc)) return false; keys.add(branch.branchKey); return true; }); });
}
export function validWorkflowInstanceRow(value: unknown): value is WorkflowInstanceListItemResponse { if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>; return base(row) && text(row.definitionKey) && idValid(row.startedById) && nullableText(row.completedAtUtc); }
export function validWorkflowInstancePage(value: unknown, page: number, size: number): value is { items: WorkflowInstanceListItemResponse[]; page: number; pageSize: number; total: number } {
  if (!value || typeof value !== 'object') return false; const result = value as Record<string, unknown>;
  return result.page === page && result.pageSize === size && Number.isSafeInteger(result.total) && Number(result.total) >= 0 && Array.isArray(result.items) && result.items.length <= size && result.items.every(validWorkflowInstanceRow) && new Set(result.items.map(row => row.id.toLowerCase())).size === result.items.length;
}
export function validWorkflowLogs(value: unknown, id: string): value is WorkflowExecutionLogResponse[] {
  return Array.isArray(value) && value.length <= 200 && value.every((value: unknown) => { if (!value || typeof value !== 'object') return false; const row = value as Record<string, unknown>; return idValid(row.id) && idValid(row.instanceId) && row.instanceId.toLowerCase() === id.toLowerCase() && (row.stepId === null || idValid(row.stepId)) && text(row.transitionKey) && nullableText(row.fromStatusKey) && text(row.toStatusKey) && text(row.createdAtUtc); }) && new Set(value.map(row => row.id.toLowerCase())).size === value.length;
}
/** 列表没有修订，不能拿其状态覆盖权威详情；仅比较实际不可变的业务绑定。 */
export const workflowInstanceBinding = (row: WorkflowInstanceResponse | WorkflowInstanceListItemResponse): string => workflowFormValueKey([row.id.toLowerCase(), row.definitionVersionId.toLowerCase(), row.businessType, row.businessId, row.businessTitle, row.startedAtUtc]);
