import { isGuid, readWorkflowReassignmentCandidatePageResponse, type WorkflowReassignmentCandidatePageResponse } from '@fullnet/client-contracts';
import { request } from './http';

/** 候选读取绑定原实例和显式待办；目录仅提供选择，实际改派仍重新核对活动成员及修订。 */
export async function listWorkflowReassignmentCandidates(instanceId: string, todoId: string, page = 1, pageSize = 20,
  signal?: AbortSignal): Promise<WorkflowReassignmentCandidatePageResponse> {
  if (!isGuid(instanceId) || !isGuid(todoId) || [instanceId, todoId].includes('00000000-0000-0000-0000-000000000000')
    || !Number.isSafeInteger(page) || !Number.isSafeInteger(pageSize) || page < 1 || pageSize < 1 || pageSize > 100
    || page > Math.floor(2_147_483_647 / pageSize)) throw Error('client.invalid_workflow_reassignment_candidates_request');
  const query = new URLSearchParams({ todoId, page: String(page), pageSize: String(pageSize) });
  const value = readWorkflowReassignmentCandidatePageResponse(await request<unknown>(
    `/api/v1/workflow/instances/${encodeURIComponent(instanceId)}/reassignment-candidates?${query}`, { method: 'GET' }, signal));
  if (value.instanceId.toLowerCase() !== instanceId.toLowerCase() || value.todoId.toLowerCase() !== todoId.toLowerCase()
    || value.page !== page || value.pageSize !== pageSize || value.items.length > pageSize || value.total < value.items.length
    || new Set(value.items.map(item => item.id.toLowerCase())).size !== value.items.length
    || value.items.some(item => item.id === '00000000-0000-0000-0000-000000000000' || !item.username.trim() || !item.displayName.trim()))
    throw Error('client.invalid_workflow_reassignment_candidates_response');
  return value;
}
