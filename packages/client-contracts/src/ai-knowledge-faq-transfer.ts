import type {AiKnowledgeFaqTransferPackage,AiKnowledgeFaqImportReport} from './generated/models.generated.js';
import {readAiKnowledgeFaqTransferPackage,readAiKnowledgeFaqImportReport} from './generated/guards.generated.js';
import {validateKnowledgeFaqSources} from './ai-knowledge-faq-drafts.js';
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hash=/^[0-9a-f]{64}$/i;
const invalid=()=>new Error('client.invalid_knowledge_faq_transfer');
/** 包仅提供严格显示形状，授权必须由导入端在当前作用域重读权威记录。 */
export function validateKnowledgeFaqTransferPackage(value:unknown):AiKnowledgeFaqTransferPackage{
  const pack=readAiKnowledgeFaqTransferPackage(value);
  if(pack.format!=='faq-transfer-v4'||pack.items.length<1||pack.items.length>8||new TextEncoder().encode(JSON.stringify(pack)).length>512*1024)throw invalid();
  const ids=new Set<string>();const bases=new Set<string>();
  for(const item of pack.items){const c=item.content;
    if(![item.faqId,item.knowledgeBaseId,item.revisionId].every(id=>uuid.test(id))||!Number.isSafeInteger(item.headVersion)||item.headVersion<1||item.headVersion>2147483647
      ||!hash.test(item.sourceHash)||!hash.test(item.contentHash)||ids.has(item.faqId.toLowerCase())||!['independent','document_derived','faq_derived','feedback_derived'].includes(item.origin)
      ||item.sources.length>8||(item.origin==='independent'?!item.basis?.trim()||item.basis.length>1024||item.sources.length!==0:item.basis!=null||item.sources.length<1)
      ||!c.question.trim()||c.question.length>512||!c.answer.trim()||c.answer.length>8000||!c.category.trim()||c.category.length>64||c.aliases.length>16
      ||c.aliases.some(alias=>!alias.trim()||alias.length>512)||[c.validFromUtc,c.expiresAtUtc].some(date=>date!=null&&!Number.isFinite(Date.parse(date))))throw invalid();
    ids.add(item.faqId.toLowerCase());bases.add(item.knowledgeBaseId.toLowerCase());const sources=new Set<string>();
    validateKnowledgeFaqSources(item.origin,item.basis,item.sources,item.graph,item.feedback,item.personalSchedules);
    for(const source of item.sources){
      if(![source.knowledgeBaseId,source.documentId,source.versionId,source.publicationId,source.generationId].every(id=>uuid.test(id))
        ||!hash.test(source.manifestHash)||![source.documentVersion,source.knowledgeBaseVersion].every(n=>Number.isSafeInteger(n)&&n>0&&n<=2147483647)||sources.has(source.documentId.toLowerCase()))throw invalid();
      sources.add(source.documentId.toLowerCase());
    }
  }
  if(bases.size!==1)throw invalid();return pack;
}
/** 响应必须逐条完整覆盖输入；失败项不能伪造新草稿身份或冒充成功。 */
export function validateKnowledgeFaqImportReport(value:unknown,count:number,committed:boolean):AiKnowledgeFaqImportReport{
  const report=readAiKnowledgeFaqImportReport(value);const statuses=committed?['created','replayed','conflict','classification_conflict','source_unavailable']:['ready','classification_conflict','source_unavailable'];
  if(!hash.test(report.digest)||count<1||count>8||report.items.length!==count)throw invalid();const ids=new Set<string>();
  for(const [i,item] of report.items.entries()){
    const success=item.status==='created'||item.status==='replayed';
    if(item.index!==i||!statuses.includes(item.status)||(success?!item.faqId||!uuid.test(item.faqId)||!item.savedRevisionId||!uuid.test(item.savedRevisionId):item.faqId!=null||item.savedRevisionId!=null))throw invalid();
    if(item.faqId){if(ids.has(item.faqId.toLowerCase()))throw invalid();ids.add(item.faqId.toLowerCase());}
  }
  return report;
}
