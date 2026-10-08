import { aiGetKnowledgeAnswerFeedback, aiGetKnowledgeAnswerFeedbackState, aiSetKnowledgeAnswerFeedback, aiWithdrawKnowledgeAnswerFeedback,
  type AiGetKnowledgeAnswerFeedbackParameters, type AiSetKnowledgeAnswerFeedbackParameters,
  type AiWithdrawKnowledgeAnswerFeedbackParameters } from '@fullnet/client-contracts';
import { http } from './http';
// 页面只绑定共享 HTTP 客户端；路径、版本参数与响应验证由生成操作负责。
export const getAnswerFeedback = (parameters: AiGetKnowledgeAnswerFeedbackParameters, signal?: AbortSignal) => aiGetKnowledgeAnswerFeedback(http, parameters, signal);
export const getAnswerFeedbackState = (parameters: AiGetKnowledgeAnswerFeedbackParameters, signal?: AbortSignal) => aiGetKnowledgeAnswerFeedbackState(http, parameters, signal);
export const setAnswerFeedback = (parameters: AiSetKnowledgeAnswerFeedbackParameters, signal?: AbortSignal) => aiSetKnowledgeAnswerFeedback(http, parameters, signal);
export const withdrawAnswerFeedback = (parameters: AiWithdrawKnowledgeAnswerFeedbackParameters, signal?: AbortSignal) => aiWithdrawKnowledgeAnswerFeedback(http, parameters, signal);
