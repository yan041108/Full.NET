import type { AiKnowledgeEvaluationMutation, AiKnowledgeEvaluationPage, AiKnowledgeEvaluationWorkspace } from './generated/models.generated.js';

const guid=/^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const hash=/^[0-9a-f]{64}$/;
const text=(value:string,max:number)=>value.length>0&&value.length<=max&&value.trim().length>0;

/** 客户端只接收人工脱敏字段；来源数量允许大于当前样例数，不能把移出样例理解成解除来源。 */
export function validateKnowledgeEvaluation(value:AiKnowledgeEvaluationWorkspace,knowledgeBaseId:string,setId?:string,revisionId?:string):AiKnowledgeEvaluationWorkspace{
  const invalid=()=>new Error('client.invalid_knowledge_evaluation');
  if(!guid.test(value.id)||!guid.test(value.knowledgeBaseId)||value.knowledgeBaseId.toLowerCase()!==knowledgeBaseId.toLowerCase()
    ||(setId!==undefined&&value.id.toLowerCase()!==setId.toLowerCase())||!Number.isSafeInteger(value.version)||value.version<1
    ||!guid.test(value.revision.id)||(revisionId!==undefined&&value.revision.id.toLowerCase()!==revisionId.toLowerCase())
    ||!Number.isSafeInteger(value.revision.number)||value.revision.number<1||!text(value.revision.name,160)
    ||!['draft','frozen'].includes(value.revision.state)||!hash.test(value.revision.contentHash)||!hash.test(value.revision.sourceHash)
    ||!Number.isSafeInteger(value.revision.sourceCount)||value.revision.sourceCount<0||value.revision.sourceCount>8
    ||!Number.isFinite(Date.parse(value.revision.createdAtUtc))||value.revision.cases.length<1||value.revision.cases.length>8
    ||!['none','approved','withdrawn'].includes(value.approvalState)||(value.approvalId===null)!==(value.approvalState==='none')
    ||(value.approvalId!==null&&!guid.test(value.approvalId))||(value.approvalState==='approved'&&value.revision.state!=='frozen')
    ||(value.canIndependentlyApprove&&value.revision.state!=='frozen')
    ||(value.revision.restoredFromRevisionId!==null&&!guid.test(value.revision.restoredFromRevisionId)))throw invalid();
  const seen=new Set<string>();
  for(const item of value.revision.cases){
    if(!guid.test(item.id)||seen.has(item.id.toLowerCase())||!['zh-CN','en-US'].includes(item.sample.languageTag)
      ||!text(item.sample.question,2048)||!text(item.sample.expectedAnswer,4096)||!text(item.sample.evidence,8192)
      ||(item.sourceKey===null?item.independentBasis===null||!text(item.independentBasis,1024)
        :!hash.test(item.sourceKey)||item.independentBasis!==null||value.revision.sourceCount===0))throw invalid();
    seen.add(item.id.toLowerCase());
  }
  return value;
}

/** 有界页不伪造完整计数；隐藏条目的扫描游标不能作为内容或授权证明。 */
export function validateKnowledgeEvaluationPage(value:AiKnowledgeEvaluationPage,knowledgeBaseId:string):AiKnowledgeEvaluationPage{
  if(value.items.length>8||(value.nextAfterId!==null&&!guid.test(value.nextAfterId))
    ||new Set(value.items.map(item=>item.id.toLowerCase())).size!==value.items.length)throw new Error('client.invalid_knowledge_evaluation');
  for(const item of value.items)validateKnowledgeEvaluation(item,knowledgeBaseId);
  return value;
}

/** 已保存修订和当前头可以不同，幂等重放不能把旧保存身份替换成当前头。 */
export function validateKnowledgeEvaluationMutation(value:AiKnowledgeEvaluationMutation,knowledgeBaseId:string):AiKnowledgeEvaluationMutation{
  if(!guid.test(value.savedRevisionId))throw new Error('client.invalid_knowledge_evaluation');
  if(value.deliveryState==='source_unavailable'){
    if(value.workspace!==null)throw new Error('client.invalid_knowledge_evaluation');
  }else if(value.deliveryState!=='available'||value.workspace===null)throw new Error('client.invalid_knowledge_evaluation');
  else validateKnowledgeEvaluation(value.workspace,knowledgeBaseId);
  return value;
}
