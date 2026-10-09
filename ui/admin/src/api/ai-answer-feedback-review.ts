import { aiGetKnowledgeAnswerFeedbackReviews, aiReviewKnowledgeAnswerFeedback, aiExportKnowledgeAnswerFeedbackLabels,
  type AiGetKnowledgeAnswerFeedbackReviewsParameters, type AiReviewKnowledgeAnswerFeedbackParameters } from '@fullnet/client-contracts';
import { http } from './http';
// 审核与标签导出单独登记路由覆盖，保留原用户反馈操作的稳定绑定。
export const getAnswerFeedbackReviews = (parameters: AiGetKnowledgeAnswerFeedbackReviewsParameters, signal?: AbortSignal) => aiGetKnowledgeAnswerFeedbackReviews(http, parameters, signal);
export const reviewAnswerFeedback = (parameters: AiReviewKnowledgeAnswerFeedbackParameters, signal?: AbortSignal) => aiReviewKnowledgeAnswerFeedback(http, parameters, signal);
export const exportAnswerFeedbackLabels = (signal?: AbortSignal) => aiExportKnowledgeAnswerFeedbackLabels(http, signal);
