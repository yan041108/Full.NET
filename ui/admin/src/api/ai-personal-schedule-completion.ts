import { aiGetPersonalScheduleCompletionProposal, aiGetPersonalScheduleCompletionStatus, aiReconcilePersonalScheduleCompletion,
  validateScheduleCompletionProposal, validateScheduleCompletionStatus } from '@fullnet/client-contracts';
import { http } from './http';

// 原操作由服务端Run冻结；对账不生成新操作或重发完成写入。
export const getScheduleCompletionProposal = async (runId: string, signal?: AbortSignal) => validateScheduleCompletionProposal(await aiGetPersonalScheduleCompletionProposal(http, { runId }, signal), runId);
export const getScheduleCompletionStatus = async (runId: string, signal?: AbortSignal) => validateScheduleCompletionStatus(await aiGetPersonalScheduleCompletionStatus(http, { runId }, signal), runId);
export const reconcileScheduleCompletion = async (runId: string, signal?: AbortSignal) => validateScheduleCompletionStatus(await aiReconcilePersonalScheduleCompletion(http, { runId }, signal), runId);
