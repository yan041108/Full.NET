import {
  workflowListMyCc,
  workflowMarkCcRead,
  type WorkflowCcReadResponse,
  type WorkflowCcResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询当前用户的有界工作流抄送列表。 */
export async function listMyWorkflowCc(
  signal?: AbortSignal
): Promise<WorkflowCcResponse[]> {
  return workflowListMyCc(http, {}, signal);
}

/** 幂等标记当前用户自己的单条工作流抄送为已读。 */
export async function markWorkflowCcRead(
  ccId: string,
  signal?: AbortSignal
): Promise<WorkflowCcReadResponse> {
  return workflowMarkCcRead(http, { ccId }, signal);
}

export type { WorkflowCcReadResponse, WorkflowCcResponse };
