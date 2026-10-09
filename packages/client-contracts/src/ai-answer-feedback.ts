// 回答反馈使用运行时 OpenAPI 生成的操作、响应校验与版本契约。
export { aiGetKnowledgeAnswerFeedback, aiGetKnowledgeAnswerFeedbackState, aiSetKnowledgeAnswerFeedback, aiWithdrawKnowledgeAnswerFeedback } from './generated/operations.generated.js';
export type { AiGetKnowledgeAnswerFeedbackParameters, AiSetKnowledgeAnswerFeedbackParameters, AiWithdrawKnowledgeAnswerFeedbackParameters } from './generated/operations.generated.js';
export type { AiKnowledgeAnswerFeedbackResponse, AiKnowledgeAnswerFeedbackStateResponse, SetAiKnowledgeAnswerFeedbackRequest } from './generated/models.generated.js';

import type { HttpClient } from './http.js';
import { aiGetKnowledgeAnswerFeedbackReviews as getReviews, aiReviewKnowledgeAnswerFeedback as review,
  aiExportKnowledgeAnswerFeedbackLabels as exportLabels } from './generated/operations.generated.js';
import type { AiGetKnowledgeAnswerFeedbackReviewsParameters, AiReviewKnowledgeAnswerFeedbackParameters } from './generated/operations.generated.js';
import type { AiKnowledgeAnswerFeedbackReviewItem, AiKnowledgeAnswerFeedbackReviewPage, AiKnowledgeAnswerFeedbackExportResponse } from './generated/models.generated.js';
export type { AiGetKnowledgeAnswerFeedbackReviewsParameters, AiReviewKnowledgeAnswerFeedbackParameters } from './generated/operations.generated.js';
export type { AiKnowledgeAnswerFeedbackReviewItem, AiKnowledgeAnswerFeedbackReviewPage, AiKnowledgeAnswerFeedbackExportResponse, AiKnowledgeAnswerFeedbackExportCell, ReviewAiKnowledgeAnswerFeedbackRequest } from './generated/models.generated.js';

const integer = (value: number, minimum: number, maximum: number) => Number.isSafeInteger(value) && value >= minimum && value <= maximum;
const labels = (rating: string, reason: string) => rating === 'helpful' ? ['accurate', 'useful'].includes(reason)
  : rating === 'unhelpful' && ['incomplete', 'unsupported', 'outdated', 'other'].includes(reason);
const uuid = (value: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value) && value !== '00000000-0000-0000-0000-000000000000';
function checkedItem(item: AiKnowledgeAnswerFeedbackReviewItem): AiKnowledgeAnswerFeedbackReviewItem {
  if (!uuid(item.feedbackId) || !labels(item.ratingKey, item.reasonKey) || !integer(item.citationCount, 0, 8)
    || !integer(item.feedbackVersion, 1, 2147483647) || !integer(item.reviewVersion, 0, 2147483647)
    || !['pending_review', 'approved', 'rejected'].includes(item.reviewStateKey)) throw new Error('invalid_ai_feedback_review_response');
  return { feedbackId: item.feedbackId, ratingKey: item.ratingKey, reasonKey: item.reasonKey, citationCount: item.citationCount,
    feedbackVersion: item.feedbackVersion, reviewStateKey: item.reviewStateKey, reviewVersion: item.reviewVersion };
}
/** 生成操作验证 HTTP 契约，此边界额外约束闭集标签、版本与有界扫描，未知字段不进入界面。 */
export async function aiGetKnowledgeAnswerFeedbackReviews(http: HttpClient, parameters: AiGetKnowledgeAnswerFeedbackReviewsParameters = {}, signal?: AbortSignal): Promise<AiKnowledgeAnswerFeedbackReviewPage> {
  if (parameters.after !== undefined && parameters.after !== null && (parameters.after.length === 0 || parameters.after.length > 2048)) throw new Error('invalid_ai_feedback_review_cursor');
  const result = await getReviews(http, parameters, signal);
  if (result.items.length > 25 || result.nextCursor != null && (result.nextCursor.length === 0 || result.nextCursor.length > 2048)) throw new Error('invalid_ai_feedback_review_page');
  const items = result.items.map(checkedItem);
  if (new Set(items.map(item => item.feedbackId)).size !== items.length) throw new Error('invalid_ai_feedback_review_page');
  return { items, nextCursor: result.nextCursor ?? null };
}
/** 只接受决定与双版本；返回内容必须绑定该命令，避免把错误行当成审核成功。 */
export async function aiReviewKnowledgeAnswerFeedback(http: HttpClient, parameters: AiReviewKnowledgeAnswerFeedbackParameters, signal?: AbortSignal): Promise<AiKnowledgeAnswerFeedbackReviewItem> {
  const body = parameters.body;
  if (!uuid(parameters.feedbackId) || !['approved', 'rejected'].includes(body.decisionKey) || !integer(body.feedbackVersion, 1, 2147483646) || !integer(body.reviewVersion, 0, 2147483646)) throw new Error('invalid_ai_feedback_review_request');
  const result = checkedItem(await review(http, { feedbackId: parameters.feedbackId, body: { decisionKey: body.decisionKey, feedbackVersion: body.feedbackVersion, reviewVersion: body.reviewVersion } }, signal));
  if (result.feedbackId !== parameters.feedbackId || result.feedbackVersion !== body.feedbackVersion || result.reviewStateKey !== body.decisionKey || result.reviewVersion !== body.reviewVersion + 1) throw new Error('invalid_ai_feedback_review_response');
  return result;
}
/** 投影后再释放统计；不序列化原始响应，避免附加字段进入下载文件。 */
export async function aiExportKnowledgeAnswerFeedbackLabels(http: HttpClient, signal?: AbortSignal): Promise<AiKnowledgeAnswerFeedbackExportResponse> {
  const result = await exportLabels(http, {}, signal);
  if (result.policyVersion !== 'feedback-labels-v1' || result.isPartial !== true || result.cells.length > 64) throw new Error('invalid_ai_feedback_export_response');
  const keys = new Set<string>(); let count = 0;
  const cells = result.cells.map(cell => {
    const key = [cell.ratingKey, cell.reasonKey, cell.citationCount].join(':');
    if (!labels(cell.ratingKey, cell.reasonKey) || !integer(cell.citationCount, 0, 8) || !integer(cell.feedbackCount, 5, 64) || keys.has(key)) throw new Error('invalid_ai_feedback_export_response');
    keys.add(key); count += cell.feedbackCount;
    return { ratingKey: cell.ratingKey, reasonKey: cell.reasonKey, citationCount: cell.citationCount, feedbackCount: cell.feedbackCount };
  });
  if (count > 64) throw new Error('invalid_ai_feedback_export_response');
  return { policyVersion: result.policyVersion, isPartial: true, cells };
}
