import {
  aiGetAgentWorkflowProposal, aiCreateAgentApproval, aiGetAgentApproval, aiDecideAgentApproval,
  type CreateAiAgentApprovalRequest, type DecideAiAgentApprovalRequest
} from '@fullnet/client-contracts';
import { http } from './http';

// 提案参数来自原运行，所有读写都保留生成 Operation 的响应守卫与当前认证上下文。
export const getAgentWorkflowProposal = (runId: string, signal?: AbortSignal) => aiGetAgentWorkflowProposal(http, { runId }, signal);
export const createAgentApproval = (body: CreateAiAgentApprovalRequest, signal?: AbortSignal) => aiCreateAgentApproval(http, { body }, signal);
export const getAgentApproval = (approvalId: string, signal?: AbortSignal) => aiGetAgentApproval(http, { approvalId }, signal);
export const decideAgentApproval = (approvalId: string, body: DecideAiAgentApprovalRequest, signal?: AbortSignal) => aiDecideAgentApproval(http, { approvalId, body }, signal);
