import {
  isWorkflowTodoDetail,
  workflowApproveTodo,
  workflowGetTodoRuntime,
  workflowListMyTodos,
  workflowRejectTodo,
  type WorkflowInstanceResponse,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoResponse
} from '@fullnet/client-contracts';
import { http } from './http';

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

/** 导出待办列表、详情、审批结果与字段补丁模型，供待办页和审批弹窗共享同一契约。 */
export type {
  WorkflowInstanceResponse,
  WorkflowSubmission,
  WorkflowTodoDetail,
  WorkflowTodoResponse
};
