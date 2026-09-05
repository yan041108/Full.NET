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

/** 分页查询当前作用域内的工作流恢复任务。 */
export async function listWorkflowRecoveryTasks(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfWorkflowRecoveryTaskResponse> {
  return workflowListRecoveryTasks(http, { page, pageSize }, signal);
}

/** 读取单条恢复任务详情。 */
export async function getWorkflowRecoveryTask(
  taskId: string,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  return workflowGetRecoveryTask(http, { taskId }, signal);
}

/** 人工重试失败或死信恢复任务，必须携带修订号、原因和幂等键。 */
export async function retryWorkflowRecoveryTask(
  taskId: string,
  body: RetryWorkflowRecoveryTaskRequest,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  return workflowRetryRecoveryTask(http, { taskId, body }, signal);
}

/** 按当前实例事实对账关闭恢复任务。 */
export async function reconcileWorkflowRecoveryTask(
  taskId: string,
  body: ReconcileWorkflowRecoveryTaskRequest,
  signal?: AbortSignal
): Promise<WorkflowRecoveryTaskResponse> {
  return workflowReconcileRecoveryTask(http, { taskId, body }, signal);
}

export type {
  ReconcileWorkflowRecoveryTaskRequest,
  RetryWorkflowRecoveryTaskRequest,
  WorkflowRecoveryTaskResponse
};
