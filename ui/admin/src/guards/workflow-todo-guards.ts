import { isWorkflowTodoDetail, type WorkflowTodoDetail, type WorkflowTodoListItemResponse, type WorkflowTodoReturnTargetResponse, type WorkflowSubmission } from '@fullnet/client-contracts';
import { workflowFormIdValid as idValid, workflowFormRevisionValid as revisionValid, workflowFormValueKey } from './workflow-form-guards';
import { instanceStatuses, instanceTrim } from './workflow-instance-guards';
export const todoStatuses = ['active', 'awaiting_before_countersign', 'awaiting_after_countersign', 'completed', 'cancelled'] as const;
export const todoResultActions = ['approve', 'reject', 'return', 'cancel', 'cancelled'] as const;
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const nullableText = (value: unknown): boolean => value === null || typeof value === 'string';
const count = (value: unknown): boolean => Number.isInteger(value) && Number(value) >= 0 && Number(value) <= 2147483647;
export function validWorkflowTodo(value: unknown): value is WorkflowTodoDetail {
  if (!isWorkflowTodoDetail(value)) return false;
  return [value.id, value.instanceId, value.stepId, value.assigneeUserId, value.formVersionId].every(idValid)
    && revisionValid(value.revision) && revisionValid(value.submissionRevision)
    && todoStatuses.includes(value.statusKey as typeof todoStatuses[number])
    && [value.requiredApprovalCount, value.approvedCount, value.rejectedCount, value.pendingCount].every(count);
}
export function validWorkflowTodoRow(value: unknown, history: boolean): value is WorkflowTodoListItemResponse {
  if (!record(value) || ![value.id, value.instanceId, value.stepId].every(idValid) || !revisionValid(value.revision)
    || !todoStatuses.includes(value.statusKey as typeof todoStatuses[number]) || !text(value.arrivedAtUtc)
    || !nullableText(value.completedAtUtc) || !nullableText(value.resultActionKey) || !nullableText(value.businessTitle)
    || ![value.businessType, value.businessId, value.definitionKey, value.nodeKey].every(text)
    || !instanceStatuses.includes(value.instanceStatusKey as typeof instanceStatuses[number])) return false;
  return history ? text(value.completedAtUtc) && text(value.resultActionKey) && !['active', 'awaiting_before_countersign'].includes(String(value.statusKey))
    : ['active', 'awaiting_before_countersign'].includes(String(value.statusKey)) && value.completedAtUtc === null && value.resultActionKey === null;
}
export function validWorkflowTodoPage(value: unknown, page: number, size: number, history: boolean): value is { items: WorkflowTodoListItemResponse[]; page: number; pageSize: number; total: number } {
  return record(value) && value.page === page && value.pageSize === size && Number.isSafeInteger(value.total) && Number(value.total) >= 0
    && Array.isArray(value.items) && value.items.length <= size && value.items.every(row => validWorkflowTodoRow(row, history))
    && new Set(value.items.map(row => row.id.toLowerCase())).size === value.items.length;
}
export function validWorkflowTodoTargets(value: unknown): value is WorkflowTodoReturnTargetResponse[] {
  return Array.isArray(value) && value.every(row => record(row) && idValid(row.stepId) && idValid(row.assigneeUserId) && text(row.nodeKey) && text(row.completedAtUtc))
    && new Set(value.map(row => row.stepId.toLowerCase())).size === value.length;
}
/** 票数与表单提交可由同一步骤的其他待办独立推进，不纳入待办自身修订的等值判断。 */
export const workflowTodoBinding = (value: WorkflowTodoDetail): string => workflowFormValueKey([value.id.toLowerCase(), value.instanceId.toLowerCase(), value.stepId.toLowerCase(), value.assigneeUserId.toLowerCase(), value.formVersionId.toLowerCase(), value.formSchemaHash, value.formSchema, value.fieldPolicies]);
export function validWorkflowTodoPatch(detail: WorkflowTodoDetail, patch: unknown): patch is WorkflowSubmission {
  if (!record(patch)) return false;
  const fields = new Map(detail.formSchema.sections.flatMap(section => section.fields.map(field => [field.fieldKey, field] as const)));
  return Object.keys(patch).every(key => { const field = fields.get(key); if (!field) return false; const policy = detail.fieldPolicies[key] ?? (field.required ? 'required' : 'editable'); return policy === 'required' || policy === 'editable'; });
}
export function workflowTodoRequiredReady(detail: WorkflowTodoDetail, patch: WorkflowSubmission): boolean {
  const merged = { ...detail.submission, ...patch };
  return detail.formSchema.sections.every(section => section.fields.every(field => {
    const policy = detail.fieldPolicies[field.fieldKey] ?? (field.required ? 'required' : 'editable');
    if (policy !== 'required') return true; const value = merged[field.fieldKey];
    return value !== null && value !== undefined && (typeof value !== 'string' || instanceTrim(value).length > 0) && (!Array.isArray(value) || value.length > 0);
  }));
}
