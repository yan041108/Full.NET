import {
  workflowListMyCc,
  workflowMarkCcRead,
  type WorkflowCcReadResponse,
  type WorkflowCcResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { workflowFormIdValid } from './workflow-form-guards';
import { validWorkflowCcList, validWorkflowCcReceipt } from './workflow-cc-guards';

/** 查询当前用户的有界工作流抄送列表。 */
export async function listMyWorkflowCc(
  signal?: AbortSignal
): Promise<WorkflowCcResponse[]> {
  const result = await workflowListMyCc(http, {}, signal);
  if (!validWorkflowCcList(result)) throw Error('client.invalid_workflow_cc_list');
  return result;
}

/** 幂等标记当前用户自己的单条工作流抄送为已读。 */
export async function markWorkflowCcRead(
  ccId: string,
  signal?: AbortSignal
): Promise<WorkflowCcReadResponse> {
  if (!workflowFormIdValid(ccId)) throw Error('client.invalid_workflow_cc_id');
  const result = await workflowMarkCcRead(http, { ccId }, signal);
  if (!validWorkflowCcReceipt(result, ccId)) throw Error('client.invalid_workflow_cc_receipt');
  return result;
}

export type { WorkflowCcReadResponse, WorkflowCcResponse };
