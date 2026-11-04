import { isGuid, isRecord } from './response-shape.js';
import { readWorkflowRecipientCandidatePageResponse } from './generated/guards.generated.js';
import type { WorkflowRecipientCandidatePageResponse } from './generated/models.generated.js';

/** 当前资源和修订随候选一起返回，提交时沿用这份事实而非猜测待办版本。 */
export interface WorkflowReassignmentCandidatePageResponse extends WorkflowRecipientCandidatePageResponse {
  readonly instanceId: string;
  readonly instanceRevision: number;
  readonly todoId: string;
  readonly todoRevision: number;
}
function revision(value: unknown): number {
  const number = typeof value === 'string' && /^[1-9]\d*$/u.test(value) ? Number(value) : value;
  if (!Number.isSafeInteger(number) || (number as number) < 1) throw Error('client.invalid_workflow_reassignment_candidates_revision');
  return number as number;
}
export function readWorkflowReassignmentCandidatePageResponse(value: unknown): WorkflowReassignmentCandidatePageResponse {
  if (!isRecord(value) || !isGuid(value.instanceId) || !isGuid(value.todoId)
    || [value.instanceId, value.todoId].includes('00000000-0000-0000-0000-000000000000'))
    throw Error('client.invalid_workflow_reassignment_candidates_response');
  return { ...readWorkflowRecipientCandidatePageResponse(value), instanceId: value.instanceId, todoId: value.todoId,
    instanceRevision: revision(value.instanceRevision), todoRevision: revision(value.todoRevision) };
}
