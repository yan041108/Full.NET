import { aiPreviewKnowledgeFaqFeedback, aiCreateFeedbackKnowledgeFaqDraft, validateKnowledgeFaqFeedbackPreview,
  validateKnowledgeFaqMutation, faqSourceKey, type AiPreviewKnowledgeFaqFeedbackParameters,
  type AiCreateFeedbackKnowledgeFaqDraftParameters, type AiKnowledgeFaqFeedbackPreview } from '@fullnet/client-contracts';
import { http } from './http';

export type FeedbackPreviewIdentity = Pick<AiKnowledgeFaqFeedbackPreview, 'sourceHash' | 'feedback' | 'sources' | 'graph'>;
export async function previewFeedbackFaq(p: AiPreviewKnowledgeFaqFeedbackParameters, signal?: AbortSignal) {
  return validateKnowledgeFaqFeedbackPreview(await aiPreviewKnowledgeFaqFeedback(http, p, signal), p.body.feedbackId);
}
/** 初始答案只来自服务器；响应必须保留预览的完整来源和反馈资格，未知提交仍由原请求键核对。 */
export async function createFeedbackFaq(p: AiCreateFeedbackKnowledgeFaqDraftParameters, expected: FeedbackPreviewIdentity, signal?: AbortSignal) {
  const value = validateKnowledgeFaqMutation(await aiCreateFeedbackKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId);
  if(value.workspace.origin !== 'feedback_derived' || value.workspace.sourceHash !== expected.sourceHash
    || JSON.stringify(value.workspace.feedback) !== JSON.stringify(expected.feedback)
    || JSON.stringify(value.workspace.graph ?? null) !== JSON.stringify(expected.graph ?? null)
    || JSON.stringify(value.workspace.sources.map(faqSourceKey).sort()) !== JSON.stringify(expected.sources.map(faqSourceKey).sort()))
    throw new Error('client.invalid_knowledge_faq_feedback');
  return value;
}
