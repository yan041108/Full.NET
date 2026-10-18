import { aiPrepareFeedbackEvaluationCandidate, aiAdmitFeedbackEvaluationCandidate,
  type AiPrepareFeedbackEvaluationCandidateParameters, type AiAdmitFeedbackEvaluationCandidateParameters } from '@fullnet/client-contracts';
import { http } from './http';

// 生成操作负责响应结构校验，短期候选凭据只在当前组件内存保留。
export const prepareFeedbackEvaluation = (parameters: AiPrepareFeedbackEvaluationCandidateParameters, signal?: AbortSignal) => aiPrepareFeedbackEvaluationCandidate(http, parameters, signal);
export const admitFeedbackEvaluation = (parameters: AiAdmitFeedbackEvaluationCandidateParameters, signal?: AbortSignal) => aiAdmitFeedbackEvaluationCandidate(http, parameters, signal);
