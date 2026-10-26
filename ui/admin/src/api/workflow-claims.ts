import { readWorkflowClaimPoolPage, readWorkflowClaimRequest, readWorkflowReleaseRequest, workflowListClaimPools, workflowClaimPool, workflowReleaseTodo, workflowAssignClaimPool, type AssignWorkflowPoolRequest,
  type WorkflowClaimRequest, type WorkflowReleaseRequest } from '@fullnet/client-contracts';
import { http } from './http';

/** 候选池只读本人固定候选，领取时仍由服务端重验当前身份来源。 */
export async function listWorkflowClaimPools(page = 1, pageSize = 20, signal?: AbortSignal) {
  return readWorkflowClaimPoolPage(await workflowListClaimPools(http, { page, pageSize }, signal));
}
export async function claimWorkflowPool(poolId: string, body: WorkflowClaimRequest, signal?: AbortSignal) {
  return workflowClaimPool(http, { poolId, body: readWorkflowClaimRequest(body) }, signal);
}
export async function releaseWorkflowTodo(todoId: string, body: WorkflowReleaseRequest, signal?: AbortSignal) {
  return workflowReleaseTodo(http, { todoId, body: readWorkflowReleaseRequest(body) }, signal);
}

/** 管理员独立分配尚未领取的池，普通领取请求不能携带目标身份。 */
export async function assignWorkflowClaimPool(poolId: string, body: AssignWorkflowPoolRequest, signal?: AbortSignal) {
  return workflowAssignClaimPool(http, { poolId, body }, signal);
}
