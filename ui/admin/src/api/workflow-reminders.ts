import { http } from './http';
import { workflowListManualReminderTargets, workflowRequestManualReminder,
  type WorkflowManualReminderTargetResponse, type WorkflowManualReminderTargetsResponse,
  type RequestWorkflowManualReminder, type WorkflowManualReminderResponse } from '@fullnet/client-contracts';

export type WorkflowReminderTarget = WorkflowManualReminderTargetResponse;
export type WorkflowReminderTargets = WorkflowManualReminderTargetsResponse;
export type WorkflowReminderRequest = RequestWorkflowManualReminder;
export type WorkflowReminderReceipt = WorkflowManualReminderResponse;

const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const integer = (value: unknown, minimum = 1, maximum = Number.MAX_SAFE_INTEGER): value is number => Number.isSafeInteger(value) && (value as number) >= minimum && (value as number) <= maximum;
const date = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) && Number.isFinite(Date.parse(value));
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const invalid = (): never => { throw new TypeError('workflow.invalid_manual_reminder'); };

/** 接收人、租户和办理状态只能由服务端资源投影提供。 */
export async function listWorkflowReminderTargets(instanceId: string, signal?: AbortSignal): Promise<WorkflowReminderTargets> {
  if (!uuid(instanceId)) return invalid();
  const value = await workflowListManualReminderTargets(http, { instanceId }, signal);
  if (!object(value) || !integer(value.instanceRevision) || !Array.isArray(value.items) || value.items.length > 512) return invalid();
  const seen = new Set<string>();
  for (const item of value.items) {
    if (!object(item) || !uuid(item.stepId) || seen.has(item.stepId) || typeof item.nodeKey !== 'string' || !item.nodeKey || item.nodeKey.length > 128 ||
      !['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(String(item.nodeTypeKey)) ||
      !integer(item.stepRevision) || !integer(item.activeTodoCount, 1, 20) || !integer(item.manualCount, 0, 20) || typeof item.canRemind !== 'boolean' ||
      (item.lastRequestedAtUtc !== null && !date(item.lastRequestedAtUtc)) || (item.manualCount === 0) !== (item.lastRequestedAtUtc === null) ||
      item.manualCount === 20 && item.canRemind) return invalid();
    seen.add(item.stepId);
  }
  return value as unknown as WorkflowReminderTargets;
}

/** 客户端提交实际节点和两个修订，绝不允许指定通知接收人。 */
export async function requestWorkflowReminder(instanceId: string, body: WorkflowReminderRequest, signal?: AbortSignal): Promise<WorkflowReminderReceipt> {
  if (!uuid(instanceId) || !uuid(body.stepId) || !integer(body.expectedInstanceRevision) || !integer(body.expectedStepRevision) ||
    typeof body.idempotencyKey !== 'string' || !body.idempotencyKey.trim() || body.idempotencyKey.length > 128 ||
    Object.keys(body).some(key => !['stepId', 'expectedInstanceRevision', 'expectedStepRevision', 'idempotencyKey'].includes(key))) return invalid();
  const value = await workflowRequestManualReminder(http, { instanceId, body }, signal);
  if (!object(value) || !uuid(value.requestId) || value.stepId !== body.stepId || value.instanceRevision !== body.expectedInstanceRevision ||
    value.stepRevision !== body.expectedStepRevision || !integer(value.manualSequence, 1, 20) || !integer(value.recipientCount, 1, 20) || !date(value.acceptedAtUtc)) return invalid();
  return value as unknown as WorkflowReminderReceipt;
}
