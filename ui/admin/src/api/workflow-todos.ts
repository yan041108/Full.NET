import {
  isWorkflowTodoDetail,
  workflowApproveTodo,
  workflowGetTodoRuntime,
  workflowListTodoReturnTargets,
  workflowListMyTodos,
  workflowRejectTodo,
  workflowReturnTodo,
  type WorkflowInstanceResponse,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoResponse,
  type WorkflowTodoReturnTargetResponse
} from '@fullnet/client-contracts';
import { http, request } from './http';

/** 查询当前用户待办列表。 */
export async function listMyWorkflowTodos(
  signal?: AbortSignal
): Promise<WorkflowTodoResponse[]> {
  return workflowListMyTodos(http, {}, signal);
}

/** 读取单个待办运行时详情，并对返回结构做失败关闭校验。 */
export async function getWorkflowTodo(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoDetail> {
  const value = await workflowGetTodoRuntime(http, { todoId }, signal);
  if (!isWorkflowTodoDetail(value)) {
    throw new Error('client.invalid_workflow_todo_detail');
  }

  return value;
}

/** 查询服务端复核后的合法历史人工审批退回目标。 */
export async function listWorkflowTodoReturnTargets(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoReturnTargetResponse[]> {
  const pageSize = 100;
  const targets: WorkflowTodoReturnTargetResponse[] = [];
  for (let page = 1; ; page += 1) {
    const batch = await workflowListTodoReturnTargets(
      http,
      { todoId, page, pageSize },
      signal
    );
    targets.push(...batch);
    if (batch.length < pageSize) {
      return targets;
    }
  }
}

/** 审批通过待办，提交字段补丁、审批意见与幂等键。 */
export async function approveWorkflowTodo(
  todoId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowApproveTodo(http, {
    todoId,
    body: { expectedRevision, fieldPatch, comment, idempotencyKey }
  }, signal);
}

/** 驳回待办，提交字段补丁、审批意见与幂等键。 */
export async function rejectWorkflowTodo(
  todoId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowRejectTodo(http, {
    todoId,
    body: { expectedRevision, fieldPatch, comment, idempotencyKey }
  }, signal);
}

/** 把当前待办退回到指定合法历史人工审批步骤。 */
export async function returnWorkflowTodo(
  todoId: string,
  targetStepId: string,
  expectedRevision: number,
  fieldPatch: WorkflowSubmission,
  comment: string,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return workflowReturnTodo(http, {
    todoId,
    body: { targetStepId, expectedRevision, fieldPatch, comment, idempotencyKey }
  }, signal);
}

export type WorkflowTodoCountersignItem = {
  itemId: string;
  sequenceNo: number;
  assigneeUserId: string;
  statusKey: string;
  todoId: string | null;
};

export type WorkflowTodoCountersignChain = {
  chainId: string;
  directionKey: 'before' | 'after';
  statusKey: string;
  items: WorkflowTodoCountersignItem[];
};

/** 读取当前待办的活动加签链。 */
export async function getWorkflowTodoCountersignChain(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoCountersignChain> {
  return request<WorkflowTodoCountersignChain>(
    `/api/v1/workflow/todos/${todoId}/countersign-chain`,
    { method: 'GET', signal }
  );
}

/** 对活动待办发起前加签或后加签。 */
export async function countersignWorkflowTodo(
  todoId: string,
  directionKey: 'before' | 'after',
  assigneeUserIds: string[],
  expectedRevision: number,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return request<WorkflowInstanceResponse>(
    `/api/v1/workflow/todos/${todoId}/countersign`,
    {
      method: 'POST',
      signal,
      body: {
        directionKey,
        assigneeUserIds,
        expectedRevision,
        comment,
        idempotencyKey
      }
    }
  );
}

/** 取消尚未完成的活动加签链。 */
export async function cancelWorkflowTodoCountersign(
  todoId: string,
  expectedRevision: number,
  comment: string | null,
  idempotencyKey: string,
  signal?: AbortSignal
): Promise<WorkflowInstanceResponse> {
  return request<WorkflowInstanceResponse>(
    `/api/v1/workflow/todos/${todoId}/countersign/cancel`,
    {
      method: 'POST',
      signal,
      body: { expectedRevision, comment, idempotencyKey }
    }
  );
}

/** 导出待办列表、详情、审批结果与字段补丁模型，供待办页和审批弹窗共享同一契约。 */
export type {
  WorkflowInstanceResponse,
  WorkflowSubmission,
  WorkflowTodoDetail,
  WorkflowTodoResponse,
  WorkflowTodoReturnTargetResponse,
  WorkflowTodoCountersignChain,
  WorkflowTodoCountersignItem
};
