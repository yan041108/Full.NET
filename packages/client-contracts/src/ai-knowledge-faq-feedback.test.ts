import { describe, it, expect } from 'vitest';
import { validateKnowledgeFaqFeedbackPreview, validateKnowledgeFaqWorkspace } from './ai-knowledge-faq-drafts.js';
import { validateKnowledgeFaqTransferPackage } from './ai-knowledge-faq-transfer.js';
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const feedback=()=>({feedbackId:id(1),feedbackVersion:2,reviewId:id(2),reviewVersion:3,messageId:id(3),lineageHash:'A'.repeat(64),answerHash:'B'.repeat(64)});
const source=()=>({knowledgeBaseId:id(10),knowledgeBaseVersion:1,documentId:id(11),documentVersion:1,versionId:id(12),publicationId:id(13),generationId:id(14),manifestHash:'C'.repeat(64),sourceKind:'document-v1',faqId:null,faqRevisionId:null});
const preview=()=>({digest:'D'.repeat(64),sourceHash:'E'.repeat(64),feedback:feedback(),answer:'复用当前已批准的原回答，不额外调用模型。',sources:[source()],graph:null});
const workspace=()=>({id:id(21),knowledgeBaseId:id(22),reservedDocumentId:id(23),state:'draft',origin:'feedback_derived',basis:null,sourceHash:'E'.repeat(64),feedback:feedback(),sources:[source()],graph:null,headVersion:1,
  revision:{id:id(24),number:1,content:{question:'人工标准问题',aliases:[],answer:'原回答',category:'私人来源',validFromUtc:null,expiresAtUtc:null},contentHash:'F'.repeat(64),createdAtUtc:'2026-10-30T00:00:00Z',restoredFromRevisionId:null}});
describe('已批准私人反馈的闭合 FAQ 来源',()=>{
  it('显式预览绑定反馈、审核版本及全部来源，不包含所属人、会话或原用户问题',()=>{
    const p=validateKnowledgeFaqFeedbackPreview(preview(),id(1));expect(p.feedback.feedbackVersion).toBe(2);expect(p.sources).toHaveLength(1);
    for(const key of ['ownerUserId','sessionId','question'])expect(p).not.toHaveProperty(key);
  });
  it.each(['feedback','review','message','answerHash','lineageHash','version','sourceHash','wrongFeedback','emptyAnswer','overlongAnswer','noSources'])('拒绝缺失或不匹配的预览 %s',fault=>{
    const p=preview();
    if(fault==='feedback')p.feedback.feedbackId='';if(fault==='review')p.feedback.reviewId='';if(fault==='message')p.feedback.messageId='';
    if(fault==='answerHash')p.feedback.answerHash='';if(fault==='lineageHash')p.feedback.lineageHash='';if(fault==='version')p.feedback.reviewVersion=0;
    if(fault==='sourceHash')p.sourceHash='';if(fault==='wrongFeedback')p.feedback.feedbackId=id(99);
    if(fault==='emptyAnswer')p.answer=' ';if(fault==='overlongAnswer')p.answer='长'.repeat(8001);if(fault==='noSources')p.sources=[];
    expect(()=>validateKnowledgeFaqFeedbackPreview(p,id(1))).toThrow();
  });
  it('后续修订不能清空或更换准确反馈资格',()=>{
    const original=validateKnowledgeFaqWorkspace(workspace(),id(22));const changed=structuredClone(workspace());changed.feedback.reviewVersion++;
    expect(()=>validateKnowledgeFaqWorkspace(changed,id(22),original)).toThrow();
    const stripped=structuredClone(workspace());Reflect.deleteProperty(stripped,'feedback');expect(()=>validateKnowledgeFaqWorkspace(stripped,id(22))).toThrow();
  });
  it('v3 传输保留反馈摘要，删除来源或降级类型不形成独立权威',()=>{
    const w=workspace();const pack={format:'faq-transfer-v4',items:[{faqId:w.id,knowledgeBaseId:w.knowledgeBaseId,revisionId:w.revision.id,headVersion:1,origin:w.origin,basis:null,sourceHash:w.sourceHash,contentHash:w.revision.contentHash,content:w.revision.content,sources:w.sources,graph:w.graph,feedback:w.feedback}]};
    expect(validateKnowledgeFaqTransferPackage(pack).items[0]?.feedback).toEqual(w.feedback);
    const stripped=structuredClone(pack);Reflect.deleteProperty(stripped.items[0]!,'feedback');expect(()=>validateKnowledgeFaqTransferPackage(stripped)).toThrow();
    const downgraded=structuredClone(pack);downgraded.items[0]!.origin='document_derived';expect(()=>validateKnowledgeFaqTransferPackage(downgraded)).toThrow();
  });
});
