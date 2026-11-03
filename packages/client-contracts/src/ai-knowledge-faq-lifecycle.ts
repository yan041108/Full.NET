import type { AiKnowledgeFaqIndexAdmission, AiKnowledgeFaqLifecycle, AiKnowledgeFaqPublication, AiKnowledgeFaqPublishedContent } from './generated/models.generated.js';
import { readAiKnowledgeFaqIndexAdmission, readAiKnowledgeFaqLifecycle, readAiKnowledgeFaqPublication, readAiKnowledgeFaqPublishedContent } from './generated/guards.generated.js';
import { faqSourceKey,validateKnowledgeFaqSources } from './ai-knowledge-faq-drafts.js';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hash = /^[0-9a-f]{64}$/i;
const integer = (n:number) => Number.isSafeInteger(n) && n >= 0 && n <= 2147483647;
const invalid = () => new Error('client.invalid_knowledge_faq');
const time = (v:string) => Number.isFinite(Date.parse(v));
const same = (a:string,b:string) => a.toLowerCase() === b.toLowerCase();
/** 修订、版本、代次及发布身份分别绑定；不能用另一条 FAQ 的成功响应填充当前页面。 */
export function validateKnowledgeFaqAdmission(value: AiKnowledgeFaqIndexAdmission, faqId:string, revisionId:string) {
  value=readAiKnowledgeFaqIndexAdmission(value);
  if (![value.faqId,value.revisionId,value.versionId,value.taskId].every(v=>uuid.test(v)) || !same(value.faqId,faqId) || !same(value.revisionId,revisionId)
    || !['pending_processing','pending','running','ready','failed','cancelled','queued','processing'].includes(value.state)) throw invalid(); return value;
}
export function validateKnowledgeFaqLifecycle(value: AiKnowledgeFaqLifecycle, faqId:string) {
  value=readAiKnowledgeFaqLifecycle(value);
  if (![value.faqId,value.documentId,value.revisionId].every(v=>uuid.test(v)) || !same(value.faqId,faqId) || !integer(value.headVersion) || value.headVersion < 1
    || !integer(value.publicationVersion) || [value.versionId,value.taskId,value.publicationId,value.publishedRevisionId,value.pendingPublicationId].some(v=>v!==null&&!uuid.test(v))
    || !['pending_processing','stale','pending','queued','running','processing','ready','failed','cancelled'].includes(value.indexState)
    || !['unpublished','published','scheduled','expired','unavailable'].includes(value.publicationState)
    || [value.effectiveFromUtc,value.effectiveToUtc].some(v=>v!==null&&!time(v))
    || (value.publicationId===null)!==(value.publishedRevisionId===null)
    || value.indexState==='ready' && (!value.versionId || !value.taskId)
    || value.publicationState==='published' && (!value.publicationId||!value.effectiveFromUtc||!value.effectiveToUtc)) throw invalid(); return value;
}
function body(value: AiKnowledgeFaqPublication | AiKnowledgeFaqPublishedContent) {
  const c=value.content;
  if (!hash.test(value.sourceHash) || !c.question.trim() || c.question.length>512 || !c.answer.trim() || c.answer.length>8000 || !c.category.trim() || c.category.length>64
    || c.aliases.length>16 || c.aliases.some(a=>!a.trim()||a.length>512) || [c.validFromUtc,c.expiresAtUtc].some(v=>v!==null&&!time(v))
    || !time(value.effectiveFromUtc)||!time(value.effectiveToUtc)||Date.parse(value.effectiveToUtc)<=Date.parse(value.effectiveFromUtc)
    || !['independent','document_derived','faq_derived','feedback_derived'].includes(value.origin) || (value.origin==='independent' ? !value.basis?.trim()||value.sources.length!==0 : value.basis!==null||value.sources.length===0)
    || value.sources.length>8 || new Set(value.sources.map(faqSourceKey)).size!==value.sources.length) throw invalid();
  validateKnowledgeFaqSources(value.origin,value.basis,value.sources,value.graph,value.feedback,value.personalSchedules);
  for (const s of value.sources) if (![s.knowledgeBaseId,s.documentId,s.versionId,s.publicationId,s.generationId].every(v=>uuid.test(v))
    || !hash.test(s.manifestHash)||!integer(s.knowledgeBaseVersion)||s.knowledgeBaseVersion<1||!integer(s.documentVersion)||s.documentVersion<1) throw invalid();
}
export function validateKnowledgeFaqPublication(value: AiKnowledgeFaqPublication, faqId:string, publicationId?:string) {
  value=readAiKnowledgeFaqPublication(value); body(value);
  if (![value.id,value.faqId,value.revisionId,value.versionId,value.generationId,value.requestedByUserId].every(v=>uuid.test(v))||!same(value.faqId,faqId)
    || publicationId!==undefined&&!same(value.id,publicationId)||value.reviewedByUserId!==null&&!uuid.test(value.reviewedByUserId)
    || !hash.test(value.snapshotHash)||!integer(value.version)||value.version<1||!integer(value.expectedPublicationVersion)
    || !['pending','approved','rejected','published','superseded','withdrawn'].includes(value.state)
    || value.reviewedByUserId!==null&&same(value.reviewedByUserId,value.requestedByUserId)) throw invalid(); return value;
}
export function validatePublishedKnowledgeFaq(value: AiKnowledgeFaqPublishedContent, faqId:string) {
  value=readAiKnowledgeFaqPublishedContent(value); body(value);
  if (![value.faqId,value.revisionId,value.publicationId].every(v=>uuid.test(v))||!same(value.faqId,faqId)) throw invalid(); return value;
}
