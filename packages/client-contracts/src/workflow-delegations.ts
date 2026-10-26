import { readWorkflowTodoDelegationStateResponse, type WorkflowTodoDelegationResponse } from './generated/index.generated.js';

export type WorkflowTodoDelegation = WorkflowTodoDelegationResponse;

/** 生成协议校验形状；额外校验收回所需的业务不变量，失败时不展示可操作的代理授权。 */
export function readWorkflowTodoDelegation(value: unknown): WorkflowTodoDelegation | null {
  const grant = readWorkflowTodoDelegationStateResponse(value).delegation;
  if (grant == null) return null;
  const zero = '00000000-0000-0000-0000-000000000000';
  if ([grant.originTodoId, grant.proxyTodoId, grant.originalAssigneeUserId, grant.proxyUserId].some(id => id === zero) ||
    grant.originTodoId === grant.proxyTodoId || grant.originalAssigneeUserId === grant.proxyUserId ||
    !['active', 'completed', 'recalled', 'reassigned', 'cancelled'].includes(grant.statusKey) ||
    !Number.isSafeInteger(grant.proxyRevision) || grant.proxyRevision < 1 ||
    !Number.isSafeInteger(grant.formRevision) || grant.formRevision < 0 || !Number.isFinite(Date.parse(grant.createdAtUtc)) ||
    grant.canRecall && (grant.statusKey !== 'active' || grant.formRevision < 1))
    throw new Error('client.invalid_workflow_todo_delegation');
  return grant;
}
