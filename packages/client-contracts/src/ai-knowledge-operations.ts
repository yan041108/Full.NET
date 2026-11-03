import type { AiKnowledgeOperationsPage } from './generated/models.generated.js';
import type { AiListKnowledgeOperationsParameters } from './generated/operations.generated.js';

const guid=/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const preparation=new Set(['not_recorded','static_revision','awaiting_upload','file_uploaded','pending_confirmation','pending','processing','completed','failed','cancelled','cleanup_pending','unknown']);
const tasks=new Set(['not_enqueued','queued','processing','lease_expired','completed','failed','cancelled','unknown']);
const reviews=new Set(['none','pending','proposed','approved','rejected','published','withdrawn','unknown']);
const publications=new Set(['none','expired_record','scheduled_record','published_record','unknown']);
const date=(value:string)=>Number.isFinite(Date.parse(value));

/** 任务完成不推导发布或来源可服务；分页身份、闭合来源及时间窗口必须与原请求一致。 */
export function validateKnowledgeOperations(value:AiKnowledgeOperationsPage,input:AiListKnowledgeOperationsParameters):AiKnowledgeOperationsPage{
  const invalid=()=>new Error('client.invalid_knowledge_operations');
  if(!date(value.fromUtc)||!date(value.toUtc)||Date.parse(value.fromUtc)>=Date.parse(value.toUtc)
    ||Date.parse(value.toUtc)-Date.parse(value.fromUtc)>31*86400000||value.view!==(input.view??'all')
    ||(input.fromUtc!=null&&Date.parse(value.fromUtc)!==Date.parse(input.fromUtc))
    ||(input.toUtc!=null&&Date.parse(value.toUtc)!==Date.parse(input.toUtc))||value.items.length>25)throw invalid();
  const seen=new Set<string>();
  for(const row of value.items){
    if(![row.knowledgeBaseId,row.documentId,row.versionId].every(id=>guid.test(id))||seen.has(row.versionId.toLowerCase())
      ||(input.knowledgeBaseId!=null&&row.knowledgeBaseId.toLowerCase()!==input.knowledgeBaseId.toLowerCase())
      ||(input.afterId!=null&&row.versionId.toLowerCase()===input.afterId.toLowerCase())
      ||!Number.isSafeInteger(row.versionNumber)||row.versionNumber<1||!Number.isSafeInteger(row.attemptCount)||row.attemptCount<0
      ||!preparation.has(row.preparationState)||!tasks.has(row.taskState)||!reviews.has(row.reviewState)||!publications.has(row.publicationState)
      ||!date(row.createdAtUtc)||(row.taskUpdatedAtUtc!==null&&!date(row.taskUpdatedAtUtc))
      ||(row.sourceKind==='document-v1'?row.faqId!==null:row.sourceKind!=='faq-revision-v1'||!row.faqId||!guid.test(row.faqId))
      ||(row.taskId!==null&&!guid.test(row.taskId))||(row.taskId===null)!==(row.taskState==='not_enqueued')
      ||row.failureCode!==((row.taskState==='failed'||row.preparationState==='failed')?'ai.knowledge.index_unavailable':null)
      ||(row.publicationId===null)!==(row.publicationVersionId===null)
      ||(row.publicationId===null?row.publicationState!=='none'||row.effectiveFromUtc!==null||row.effectiveToUtc!==null
        :!guid.test(row.publicationId)||!row.publicationVersionId||!guid.test(row.publicationVersionId)
          ||!row.effectiveFromUtc||!date(row.effectiveFromUtc)||!row.effectiveToUtc||!date(row.effectiveToUtc)
          ||Date.parse(row.effectiveFromUtc)>=Date.parse(row.effectiveToUtc)))throw invalid();
    seen.add(row.versionId.toLowerCase());
  }
  if(value.nextAfterId!==null&&(value.items.length!==25||value.nextAfterId!==value.items[24]!.versionId||!guid.test(value.nextAfterId)))throw invalid();
  return value;
}
