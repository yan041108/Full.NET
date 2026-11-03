import type { AiKnowledgeFaqFeedbackSource, AiKnowledgeFaqFeedbackPreview, AiKnowledgeFaqSourceGraph, AiKnowledgeFaqDraftWorkspace, AiKnowledgeFaqDraftMutation, AiKnowledgeFaqDraftPage, AiKnowledgeFaqDraftRevision, AiKnowledgeFaqDraftRevisionPage, AiKnowledgeSearchSource } from './generated/models.generated.js';
import { readAiKnowledgeFaqFeedbackPreview, readAiKnowledgeFaqDraftWorkspace, readAiKnowledgeFaqDraftMutation, readAiKnowledgeFaqDraftPage, readAiKnowledgeFaqDraftRevisionPage } from './generated/guards.generated.js';
import type { AiPersonalScheduleSourceSelection } from './generated/models.generated.js';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hash = /^[0-9a-f]{64}$/i;
const positive = (n: number) => Number.isSafeInteger(n) && n > 0 && n <= 2147483647;
const invalid = () => new Error('client.invalid_knowledge_faq');
const keys = ['knowledgeBaseId', 'knowledgeBaseVersion', 'documentId', 'documentVersion', 'versionId', 'publicationId', 'generationId', 'manifestHash'] as const;
export const faqSourceKey = (s: AiKnowledgeSearchSource) => JSON.stringify([...keys.map(k => typeof s[k] === 'string' ? String(s[k]).toLowerCase() : s[k]),s.sourceKind??'document-v1',s.faqId?.toLowerCase()??null,s.faqRevisionId?.toLowerCase()??null]);
/** 平面父图保留当前资格身份和父边，拒绝环、孤点、截断及来源类型降级。 */
export function validateKnowledgeFaqSources(origin:string,basis:string|null|undefined,sources:AiKnowledgeSearchSource[],graph?:AiKnowledgeFaqSourceGraph|null,feedback?:AiKnowledgeFaqFeedbackSource|null,business?:AiPersonalScheduleSourceSelection[]|null){
  if(!['independent','document_derived','faq_derived','feedback_derived'].includes(origin)||sources.length>8
    ||(origin==='independent'?!basis?.trim()||basis.length>1024||sources.length!==0:basis!=null||sources.length===0)
    ||(origin==='faq_derived'?!graph:origin!=='feedback_derived'&&graph!=null)
    ||(origin==='feedback_derived')!==(feedback!=null))throw invalid();
  if(feedback)validateKnowledgeFaqFeedbackSource(feedback);
  if(business!=null && (origin==='independent'||business.length<1||business.length>7||sources.length+business.length>8
    ||new Set(business.map(value=>value.id.toLowerCase())).size!==business.length
    ||business.some(value=>!uuid.test(value.id)||!positive(value.version)||!/^[0-9A-F]{64}$/.test(value.projectionHash))))throw invalid();
  const ids=new Set<string>();
  for(const s of sources){const kind=s.sourceKind??'document-v1';
    if(![s.knowledgeBaseId,s.documentId,s.versionId,s.publicationId,s.generationId].every(v=>uuid.test(v))||!positive(s.documentVersion)||!positive(s.knowledgeBaseVersion)||!hash.test(s.manifestHash)
      ||ids.has(s.documentId.toLowerCase())||(kind==='document-v1'?s.faqId!=null||s.faqRevisionId!=null:kind!=='faq-revision-v1'||!s.faqId||!uuid.test(s.faqId)||!s.faqRevisionId||!uuid.test(s.faqRevisionId)))throw invalid();
    ids.add(s.documentId.toLowerCase());
  }
  if(!graph){if(sources.some(s=>(s.sourceKind??'document-v1')!=='document-v1'))throw invalid();return;}
  if(graph.roots.length<1||graph.roots.length>4||graph.nodes.length<1||graph.nodes.length>4||new Set(graph.roots.map(v=>v.toLowerCase())).size!==graph.roots.length)throw invalid();
  const nodes=new Map(graph.nodes.map(n=>[n.source.documentId.toLowerCase(),n]));const seen=new Set<string>();
  if(nodes.size!==graph.nodes.length||sources.filter(s=>s.sourceKind==='faq-revision-v1').length!==nodes.size)throw invalid();
  for(const node of graph.nodes)if(node.source.sourceKind!=='faq-revision-v1'||!sources.some(s=>faqSourceKey(s)===faqSourceKey(node.source))||node.parentDocumentIds.length>4||new Set(node.parentDocumentIds.map(v=>v.toLowerCase())).size!==node.parentDocumentIds.length)throw invalid();
  function visit(id:string,path:Set<string>,depth:number){id=id.toLowerCase();const node=nodes.get(id);if(depth>4||!node||path.has(id))throw invalid();seen.add(id);const next=new Set(path);next.add(id);for(const parent of node.parentDocumentIds)visit(parent,next,depth+1);}
  for(const root of graph.roots)visit(root,new Set(),1);if(seen.size!==nodes.size)throw invalid();
}
function revision(r: AiKnowledgeFaqDraftRevision) {
  const c = r.content;
  if (!uuid.test(r.id) || !positive(r.number) || !hash.test(r.contentHash) || !Number.isFinite(Date.parse(r.createdAtUtc))
    || r.restoredFromRevisionId != null && !uuid.test(r.restoredFromRevisionId) || !c.question.trim() || c.question.length > 512
    || !c.answer.trim() || c.answer.length > 8000 || !c.category.trim() || c.category.length > 64 || c.aliases.length > 16
    || c.aliases.some(a => !a.trim() || a.length > 512) || [c.validFromUtc, c.expiresAtUtc].some(d => d != null && !Number.isFinite(Date.parse(d)))) throw invalid();
}
/** 生成守卫之后再绑定身份、原始来源与头；重放身份可以早于当前头，不能伪装为新保存。 */
export function validateKnowledgeFaqWorkspace(value: AiKnowledgeFaqDraftWorkspace, baseId: string, previous?: AiKnowledgeFaqDraftWorkspace): AiKnowledgeFaqDraftWorkspace {
  value = readAiKnowledgeFaqDraftWorkspace(value); revision(value.revision);
  if (!uuid.test(value.id) || value.knowledgeBaseId.toLowerCase() !== baseId.toLowerCase() || !uuid.test(value.reservedDocumentId)
    || value.id === value.reservedDocumentId || value.state !== 'draft' || !hash.test(value.sourceHash) || !positive(value.headVersion)
    || value.revision.number !== value.headVersion || value.sources.length > 8 || !['independent', 'document_derived','faq_derived','feedback_derived'].includes(value.origin)
    || (value.origin === 'independent' ? !value.basis?.trim() || value.basis.length > 1024 || value.sources.length !== 0 : value.basis != null || value.sources.length === 0)) throw invalid();
  const sourceIds = new Set<string>();
  validateKnowledgeFaqSources(value.origin,value.basis,value.sources,value.graph,value.feedback,value.personalSchedules);
  for (const s of value.sources) {
    if (![s.knowledgeBaseId,s.documentId,s.versionId,s.publicationId,s.generationId].every(id => uuid.test(id)) || !positive(s.knowledgeBaseVersion)
      || !positive(s.documentVersion) || !hash.test(s.manifestHash) || sourceIds.has(s.documentId.toLowerCase())) throw invalid();
    sourceIds.add(s.documentId.toLowerCase());
  }
  if (previous && (value.origin !== previous.origin || value.basis !== previous.basis || value.sourceHash !== previous.sourceHash
    || JSON.stringify(value.sources.map(faqSourceKey).sort()) !== JSON.stringify(previous.sources.map(faqSourceKey).sort()) || JSON.stringify(value.graph??null)!==JSON.stringify(previous.graph??null)
    || JSON.stringify(value.feedback??null)!==JSON.stringify(previous.feedback??null)
    || JSON.stringify(value.personalSchedules??null)!==JSON.stringify(previous.personalSchedules??null))) throw invalid();
  return value;
}
/** 私人来源的准确反馈/审核/原消息摘要不可丢失，公开响应不包含原所属人或会话。 */
export function validateKnowledgeFaqFeedbackSource(value: AiKnowledgeFaqFeedbackSource) {
  if(![value.feedbackId,value.reviewId,value.messageId].every(id=>uuid.test(id))||!positive(value.feedbackVersion)||!positive(value.reviewVersion)
    ||!hash.test(value.lineageHash)||!hash.test(value.answerHash))throw invalid();
  return value;
}
/** 预览只交付当前已核证的原回答和完整来源，不接受客户端引用子集或问题原文。 */
export function validateKnowledgeFaqFeedbackPreview(value:unknown,feedbackId:string):AiKnowledgeFaqFeedbackPreview{
  const preview=readAiKnowledgeFaqFeedbackPreview(value);validateKnowledgeFaqFeedbackSource(preview.feedback);
  if(preview.feedback.feedbackId.toLowerCase()!==feedbackId.toLowerCase()||!hash.test(preview.digest)||!hash.test(preview.sourceHash)||!preview.answer.trim()||preview.answer.length>8000)throw invalid();
  validateKnowledgeFaqSources('feedback_derived',null,preview.sources,preview.graph,preview.feedback,preview.personalSchedules);return preview;
}
export function validateKnowledgeFaqMutation(value: AiKnowledgeFaqDraftMutation, baseId: string, previous?: AiKnowledgeFaqDraftWorkspace) {
  value = readAiKnowledgeFaqDraftMutation(value); validateKnowledgeFaqWorkspace(value.workspace, baseId, previous);
  if (!uuid.test(value.savedRevisionId) || !value.replayed && value.savedRevisionId !== value.workspace.revision.id) throw invalid();
  return value;
}
export function validateKnowledgeFaqPage(value: AiKnowledgeFaqDraftPage, baseId: string, afterId?: string) {
  value = readAiKnowledgeFaqDraftPage(value); const ids = new Set<string>();
  for (const w of value.items) { validateKnowledgeFaqWorkspace(w, baseId); if (ids.has(w.id) || w.id === afterId) throw invalid(); ids.add(w.id); }
  if (value.items.length > 25 || value.nextAfterId != null && (!uuid.test(value.nextAfterId) || value.nextAfterId === afterId)) throw invalid(); return value;
}
export function validateKnowledgeFaqHistory(value: AiKnowledgeFaqDraftRevisionPage, head: AiKnowledgeFaqDraftWorkspace, afterId?: string) {
  value = readAiKnowledgeFaqDraftRevisionPage(value); let last = head.headVersion + 1; const ids = new Set<string>();
  for (const r of value.items) { revision(r); if (r.number >= last || ids.has(r.id) || r.id === afterId) throw invalid(); last = r.number; ids.add(r.id); }
  if (value.items.length > 25 || value.nextAfterId != null && (!uuid.test(value.nextAfterId) || value.nextAfterId === afterId || value.nextAfterId !== value.items.at(-1)?.id)) throw invalid(); return value;
}
