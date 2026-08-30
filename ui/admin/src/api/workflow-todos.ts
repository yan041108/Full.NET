import {
  isWorkflowTodoDetail,
  workflowApproveTodo,
  workflowGetTodo,
  workflowListMyTodos,
  workflowRejectTodo,
  type WorkflowInstanceResponse,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listMyWorkflowTodos(
  signal?: AbortSignal
): Promise<WorkflowTodoResponse[]> {
  return workflowListMyTodos(http, {}, signal);
}

export async function getWorkflowTodo(
  todoId: string,
  signal?: AbortSignal
): Promise<WorkflowTodoDetail> {
  const value = await workflowGetTodo(http, { todoId }, signal);
  if (!isWorkflowTodoDetail(value)) {
    throw new Error('client.invalid_workflow_todo_detail');
  }

  return value;
}

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

export type {
  WorkflowInstanceResponse,
  WorkflowSubmission,
  WorkflowTodoDetail,
  WorkflowTodoResponse
};
