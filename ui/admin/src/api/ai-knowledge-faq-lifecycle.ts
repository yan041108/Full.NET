import { aiIndexKnowledgeFaqRevision, aiGetKnowledgeFaqLifecycle, aiRequestKnowledgeFaqPublication, aiGetKnowledgeFaqPublication,
  aiReviewKnowledgeFaqPublication, aiPublishKnowledgeFaqPublication, aiWithdrawKnowledgeFaqPublication, aiGetPublishedKnowledgeFaq,
  validateKnowledgeFaqAdmission, validateKnowledgeFaqLifecycle, validateKnowledgeFaqPublication, validatePublishedKnowledgeFaq,
  type AiIndexKnowledgeFaqRevisionParameters, type AiGetKnowledgeFaqLifecycleParameters, type AiRequestKnowledgeFaqPublicationParameters,
  type AiGetKnowledgeFaqPublicationParameters, type AiReviewKnowledgeFaqPublicationParameters, type AiPublishKnowledgeFaqPublicationParameters,
  type AiWithdrawKnowledgeFaqPublicationParameters, type AiGetPublishedKnowledgeFaqParameters } from '@fullnet/client-contracts';
import { http } from './http';
export async function indexFaq(p:AiIndexKnowledgeFaqRevisionParameters, signal?:AbortSignal) { return validateKnowledgeFaqAdmission(await aiIndexKnowledgeFaqRevision(http,p,signal),p.faqId,p.body.expectedRevisionId); }
export async function getFaqLifecycle(p:AiGetKnowledgeFaqLifecycleParameters, signal?:AbortSignal) { return validateKnowledgeFaqLifecycle(await aiGetKnowledgeFaqLifecycle(http,p,signal),p.faqId); }
export async function requestFaqPublication(p:AiRequestKnowledgeFaqPublicationParameters, signal?:AbortSignal) {
  const value=validateKnowledgeFaqPublication(await aiRequestKnowledgeFaqPublication(http,p,signal),p.faqId);
  if(value.revisionId!==p.body.expectedRevisionId||value.versionId!==p.body.versionId||value.expectedPublicationVersion!==p.body.expectedPublicationVersion) throw new Error('client.invalid_knowledge_faq'); return value;
}
export async function getFaqPublication(p:AiGetKnowledgeFaqPublicationParameters, signal?:AbortSignal) { return validateKnowledgeFaqPublication(await aiGetKnowledgeFaqPublication(http,p,signal),p.faqId,p.publicationId); }
export async function reviewFaqPublication(p:AiReviewKnowledgeFaqPublicationParameters, signal?:AbortSignal) { return validateKnowledgeFaqPublication(await aiReviewKnowledgeFaqPublication(http,p,signal),p.faqId,p.publicationId); }
export async function publishFaqPublication(p:AiPublishKnowledgeFaqPublicationParameters, signal?:AbortSignal) { return validateKnowledgeFaqPublication(await aiPublishKnowledgeFaqPublication(http,p,signal),p.faqId,p.publicationId); }
export async function withdrawFaqPublication(p:AiWithdrawKnowledgeFaqPublicationParameters, signal?:AbortSignal) {
  const value=await aiWithdrawKnowledgeFaqPublication(http,p,signal);
  if(value.publicationId!==null||value.version!==p.body.expectedHeadVersion+1) throw new Error('client.invalid_knowledge_faq'); return value;
}
export async function getPublishedFaq(p:AiGetPublishedKnowledgeFaqParameters, signal?:AbortSignal) { return validatePublishedKnowledgeFaq(await aiGetPublishedKnowledgeFaq(http,p,signal),p.faqId); }
