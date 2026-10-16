import {
  workflowGetRecoveryTask,
  workflowListRecoveryTasks,
  workflowReconcileRecoveryTask,
  workflowRetryRecoveryTask,
  type PagedResultOfWorkflowRecoveryTaskResponse,
  type ReconcileWorkflowRecoveryTaskRequest,
  type RetryWorkflowRecoveryTaskRequest,
  type WorkflowRecoveryTaskResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { workflowFormIdValid, workflowFormWriteRevisionValid } from './workflow-form-guards';
import { recoveryTrim, validRecoveryPage, validRecoveryTask } from './workflow-recovery-guards';

function requireTaskId(taskId: string): void { if (!workflowFormIdValid(taskId)) throw Error('client.invalid_recovery_task_id'); }
function assertTask(value: unknown, taskId: string): asserts value is WorkflowRecoveryTaskResponse {
  if (!validRecoveryTask(value) || value.id.toLowerCase() !== taskId.toLowerCase()) throw Error('client.invalid_recovery_task');
}
function frozenBody(body: ReconcileWorkflowRecoveryTaskRequest, requireReason: boolean): ReconcileWorkflowRecoveryTaskRequest {
  const expectedRevision = body.expectedRevision, reason = typeof body.reason === 'string' ? recoveryTrim(body.reason) || null : body.reason,
    idempotencyKey = typeof body.idempotencyKey === 'string' ? recoveryTrim(body.idempotencyKey) : '';
  if (!workflowFormWriteRevisionValid(expectedRevision) || reason !== null && typeof reason !== 'string' || reason && reason.length > 512 || requireReason && !reason || !idempotencyKey || idempotencyKey.length > 128) throw Error('client.invalid_recovery_request');
  return { expectedRevision, reason, idempotencyKey };
}

/** 分页查询当前作用域内的工作流恢复任务。 */
export async function listWorkflowRecoveryTasks(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfWorkflowRecoveryTaskResponse> {
  if (!Number.isInteger(page) || !Number.isInteger(pageSize)) throw Error('client.invalid_recovery_page');
  page = Math.max(1, page); pageSize = Math.min(100, Math.max(1, pageSize));
  if ((page - 1) * pageSize > 2147483647) throw Error('client.invalid_recovery_page');
  const result = await workflowListRecoveryTasks(http, { page, pageSize }, signal);
  if (!validRecoveryPage(result, page, pageSize)) throw Error('client.invalid_recovery_page');
  return result;
}

/** 读取单条恢复任务详情。 */
export async function getWorkflowRecoveryTask(
  taskId: string,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  requireTaskId(taskId); const result = await workflowGetRecoveryTask(http, { taskId }, signal); assertTask(result, taskId); return result;
}

/** 人工重试失败或死信恢复任务，必须携带修订号、原因和幂等键。 */
export async function retryWorkflowRecoveryTask(
  taskId: string,
  body: RetryWorkflowRecoveryTaskRequest,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  requireTaskId(taskId); const snapshot = frozenBody(body, true) as RetryWorkflowRecoveryTaskRequest;
  const result = await workflowRetryRecoveryTask(http, { taskId, body: snapshot }, signal); assertTask(result, taskId);
  if (result.revision <= snapshot.expectedRevision) throw Error('client.invalid_recovery_receipt');
  return result;
}

/** 按当前实例事实对账关闭恢复任务。 */
export async function reconcileWorkflowRecoveryTask(
  taskId: string,
  body: ReconcileWorkflowRecoveryTaskRequest,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  requireTaskId(taskId); const snapshot = frozenBody(body, false);
  const result = await workflowReconcileRecoveryTask(http, { taskId, body: snapshot }, signal); assertTask(result, taskId);
  if (result.revision <= snapshot.expectedRevision || result.statusKey !== 'succeeded' && result.statusKey !== 'cancelled') throw Error('client.invalid_recovery_receipt');
  return result;
}

export type {
  ReconcileWorkflowRecoveryTaskRequest,
  RetryWorkflowRecoveryTaskRequest,
  WorkflowRecoveryTaskResponse
};
