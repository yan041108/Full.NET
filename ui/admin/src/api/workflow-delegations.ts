import { workflowDelegateTodo, workflowGetTodoDelegation, workflowListTodoDelegationCandidates,
  workflowRecallTodoDelegation, readWorkflowTodoDelegation, type WorkflowTodoDelegation } from '@fullnet/client-contracts';
import { http } from './http';

export type { WorkflowTodoDelegation };

/** 代理候选只绑定本人当前待办，服务端决定作用域及发布动作权限。 */
export function listWorkflowTodoDelegationCandidates(todoId: string, page = 1, pageSize = 20, signal?: AbortSignal) {
  return workflowListTodoDelegationCandidates(http, { todoId, page, pageSize }, signal);
}

/** 独立代理动作不提交表单补丁，不继承委托人的其他平台权限。 */
export function delegateWorkflowTodo(todoId: string, assigneeUserId: string, expectedRevision: number,
  expectedFormRevision: number, comment: string | null, idempotencyKey: string, signatureFileId?: string, signal?: AbortSignal) {
  return workflowDelegateTodo(http, { todoId, body: { assigneeUserId, expectedRevision, expectedFormRevision,
    comment, idempotencyKey, signatureFileId } }, signal);
}

/** 只返回本人参与的代理事实；未参与代理时返回显式空值。 */
export async function getWorkflowTodoDelegation(todoId: string, signal?: AbortSignal): Promise<WorkflowTodoDelegation | null> {
  return readWorkflowTodoDelegation(await workflowGetTodoDelegation(http, { todoId }, signal));
}

/** 收回目标由服务端原授权确定，修订来自最新代理状态，不能伪造其他代理待办。 */
export function recallWorkflowTodoDelegation(todoId: string, expectedRevision: number, expectedFormRevision: number,
  comment: string | null, idempotencyKey: string, signal?: AbortSignal) {
  return workflowRecallTodoDelegation(http, { todoId, body: { expectedRevision, expectedFormRevision, comment, idempotencyKey } }, signal);
}
