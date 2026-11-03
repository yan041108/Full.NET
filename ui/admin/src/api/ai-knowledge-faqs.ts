import { aiListKnowledgeFaqDrafts, aiGetKnowledgeFaqDraft, aiCreateIndependentKnowledgeFaqDraft, aiCreateDocumentKnowledgeFaqDraft,
  aiCreateDerivedKnowledgeFaqDraft, aiSaveKnowledgeFaqDraft, aiRestoreKnowledgeFaqDraft, aiCopyKnowledgeFaqDraft, aiListKnowledgeFaqDraftRevisions,
  validateKnowledgeFaqWorkspace, validateKnowledgeFaqMutation, validateKnowledgeFaqPage, validateKnowledgeFaqHistory, faqSourceKey,
  type AiListKnowledgeFaqDraftsParameters, type AiGetKnowledgeFaqDraftParameters, type AiCreateIndependentKnowledgeFaqDraftParameters,
  type AiCreateDerivedKnowledgeFaqDraftParameters, type AiCreateDocumentKnowledgeFaqDraftParameters, type AiSaveKnowledgeFaqDraftParameters, type AiRestoreKnowledgeFaqDraftParameters,
  type AiCopyKnowledgeFaqDraftParameters, type AiListKnowledgeFaqDraftRevisionsParameters, type AiKnowledgeFaqDraftWorkspace } from '@fullnet/client-contracts';
import { http } from './http';
const businessKeys=(values:AiKnowledgeFaqDraftWorkspace['personalSchedules']) => values==null?null:values.map(value=>`${value.id.toLowerCase()}:${value.version}:${value.projectionHash}`).sort();
export async function listFaqDrafts(p: AiListKnowledgeFaqDraftsParameters, signal?: AbortSignal) { return validateKnowledgeFaqPage(await aiListKnowledgeFaqDrafts(http, p, signal), p.knowledgeBaseId, p.afterId); }
export async function getFaqDraft(p: AiGetKnowledgeFaqDraftParameters, signal?: AbortSignal) {
  const w = validateKnowledgeFaqWorkspace(await aiGetKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId); if (w.id !== p.faqId) throw new Error('client.invalid_knowledge_faq'); return w;
}
export async function createIndependentFaq(p: AiCreateIndependentKnowledgeFaqDraftParameters, signal?: AbortSignal) {
  const r = validateKnowledgeFaqMutation(await aiCreateIndependentKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId);
  if (r.workspace.origin !== 'independent' || r.workspace.basis !== p.body.basis.trim()) throw new Error('client.invalid_knowledge_faq'); return r;
}
export async function createDocumentFaq(p: AiCreateDocumentKnowledgeFaqDraftParameters, signal?: AbortSignal) {
  const r = validateKnowledgeFaqMutation(await aiCreateDocumentKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId);
  if (JSON.stringify(businessKeys(r.workspace.personalSchedules))!==JSON.stringify(businessKeys(p.body.selection.personalSchedules))) throw new Error('client.invalid_knowledge_faq');
  if (r.workspace.origin !== 'document_derived' || JSON.stringify(r.workspace.sources.map(faqSourceKey).sort()) !== JSON.stringify(p.body.selection.sources.map(faqSourceKey).sort())) throw new Error('client.invalid_knowledge_faq'); return r;
}
export async function createDerivedFaq(p:AiCreateDerivedKnowledgeFaqDraftParameters,signal?:AbortSignal){
  const r=validateKnowledgeFaqMutation(await aiCreateDerivedKnowledgeFaqDraft(http,p,signal),p.knowledgeBaseId);
  if(p.body.selection.personalSchedules?.some(expected=>!r.workspace.personalSchedules?.some(value=>value.id.toLowerCase()===expected.id.toLowerCase()&&value.version===expected.version&&value.projectionHash===expected.projectionHash)))throw new Error('client.invalid_knowledge_faq');
  const roots=p.body.selection.sources.filter(s=>s.sourceKind==='faq-revision-v1').map(s=>s.documentId.toLowerCase()).sort();
  if(r.workspace.origin!=='faq_derived'||!r.workspace.graph||JSON.stringify(roots)!==JSON.stringify(r.workspace.graph.roots.map(id=>id.toLowerCase()).sort())
    ||p.body.selection.sources.some(s=>!r.workspace.sources.some(actual=>faqSourceKey(actual)===faqSourceKey(s))))throw new Error('client.invalid_knowledge_faq');return r;
}
export async function saveFaqDraft(p: AiSaveKnowledgeFaqDraftParameters, before: AiKnowledgeFaqDraftWorkspace, signal?: AbortSignal) {
  const r = validateKnowledgeFaqMutation(await aiSaveKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId, before); if (r.workspace.id !== p.faqId || !r.replayed && r.workspace.headVersion !== p.body.expectedHeadVersion+1) throw new Error('client.invalid_knowledge_faq'); return r;
}
export async function restoreFaqDraft(p: AiRestoreKnowledgeFaqDraftParameters, before: AiKnowledgeFaqDraftWorkspace, signal?: AbortSignal) {
  const r = validateKnowledgeFaqMutation(await aiRestoreKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId, before); if (r.workspace.id !== p.faqId || !r.replayed && (r.workspace.headVersion !== p.body.expectedHeadVersion+1 || r.workspace.revision.restoredFromRevisionId !== p.body.revisionId)) throw new Error('client.invalid_knowledge_faq'); return r;
}
export async function copyFaqDraft(p: AiCopyKnowledgeFaqDraftParameters, before: AiKnowledgeFaqDraftWorkspace, signal?: AbortSignal) {
  const r = validateKnowledgeFaqMutation(await aiCopyKnowledgeFaqDraft(http, p, signal), p.knowledgeBaseId, before); if (r.workspace.id === p.faqId) throw new Error('client.invalid_knowledge_faq'); return r;
}
export async function listFaqRevisions(p: AiListKnowledgeFaqDraftRevisionsParameters, before: AiKnowledgeFaqDraftWorkspace, signal?: AbortSignal) {
  return validateKnowledgeFaqHistory(await aiListKnowledgeFaqDraftRevisions(http, p, signal), before, p.afterId);
}
