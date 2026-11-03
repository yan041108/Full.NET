import type {AiKnowledgeEvaluationStrategy,AiKnowledgeEvaluationRun,AiKnowledgeEvaluationCaseResult,AiKnowledgeFinanceReport,AiKnowledgeQualityObservation,AiKnowledgeEvaluationRunAdmission} from './generated/models.generated.js';
import {readAiKnowledgeEvaluationStrategy,readAiKnowledgeEvaluationRun,readAiKnowledgeEvaluationCaseResult,readAiKnowledgeFinanceReport,readAiKnowledgeQualityObservation,readAiKnowledgeEvaluationRunAdmission} from './generated/guards.generated.js';
import {isKnowledgeSourceIdentity,sameKnowledgeSource} from './ai-knowledge-source-identity.js';
import {isPublishedSearchHit} from './ai-knowledge-search-validation.js';
const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const hash=/^[0-9a-f]{64}$/;
const positive=(v:number)=>Number.isSafeInteger(v)&&v>0;
const count=(v:number)=>Number.isSafeInteger(v)&&v>=0;
const duration=(v:unknown):v is number|null=>v===null||typeof v==='number'&&Number.isFinite(v)&&v>=0&&v<=86_400_000;
const amount=(v:unknown)=>v===null||typeof v==='string'&&v.length<=64&&/^(0|[1-9]\d*)(\.\d{1,28})?$/.test(v);
const invalid=()=>new Error('client.invalid_knowledge_evaluation_run');
const protocol='retrieval_pair_observed_v1';

/** 策略只接受闭合模式、准确批准身份和完整来源，不能把来源或模型版本从返回中删除。 */
export function validateEvaluationStrategy(input:AiKnowledgeEvaluationStrategy,knowledge:string):AiKnowledgeEvaluationStrategy{
 const v=readAiKnowledgeEvaluationStrategy(input);
 if(v.knowledgeBaseId!==knowledge||![v.id,v.revisionId,v.setId,v.setRevisionId,v.setApprovalId].every(id=>guid.test(id))||!positive(v.version)||!positive(v.setVersion)
  ||v.protocol!==protocol||!['keyword/vector','keyword/rewrite_rerank','vector/rewrite_rerank'].includes(v.baseline+'/'+v.candidate)
  ||![v.setContentHash,v.setSourceHash,v.definitionHash].every(x=>hash.test(x))||v.sources.length<1||v.sources.length>8
  ||new Set(v.sources.map(x=>x.documentId)).size!==v.sources.length||v.sources.some(x=>!isKnowledgeSourceIdentity(x)))throw invalid();
 const model=(m:typeof v.embeddingModel,purpose:string)=>guid.test(m.id)&&positive(m.version)&&m.purpose===purpose&&hash.test(m.destinationHash);
 if(!model(v.embeddingModel,'embedding')||(v.candidate==='vector'?v.processingModel!==null:v.processingModel===null||!model(v.processingModel,'processing')))throw invalid();
 return v;
}
/** 预览的调用上界是准确冻结样例数乘以闭合阶段数，不认证供应商金额。 */
export function validateEvaluationAdmission(input:AiKnowledgeEvaluationRunAdmission,strategy:AiKnowledgeEvaluationStrategy):AiKnowledgeEvaluationRunAdmission{
 const v=readAiKnowledgeEvaluationRunAdmission(input);
 if(v.strategyId!==strategy.id||v.strategyRevisionId!==strategy.revisionId||v.strategyVersion!==strategy.version||v.setRevisionId!==strategy.setRevisionId
  ||!hash.test(v.digest)||!positive(v.caseCount)||v.caseCount>8||v.maximumModelCalls!==v.caseCount*(strategy.candidate==='vector'?1:5)
  ||v.measurementProtocol!==protocol||v.budgetPolicy!=='existing_per_operation_budget_v1')throw invalid();return v;
}
/** 分母包含全部状态；平均与最近秩P95只来自实际测量，未记录不能转换为零。 */
export function validateEvaluationRun(input:AiKnowledgeEvaluationRun,knowledge:string,run?:string):AiKnowledgeEvaluationRun{
 const v=readAiKnowledgeEvaluationRun(input);
 if(v.knowledgeBaseId!==knowledge||(run!==undefined&&v.id!==run)||![v.id,v.strategyId,v.strategyRevisionId,v.setRevisionId].every(x=>guid.test(x))||!positive(v.version)
  ||!positive(v.total)||v.total>8||v.cases.length!==v.total||new Set(v.cases.map(x=>x.id)).size!==v.total||new Set(v.cases.map(x=>x.sampleId)).size!==v.total)throw invalid();
 for(const [index,item]of v.cases.entries())if(!guid.test(item.id)||!guid.test(item.sampleId)||item.sequence!==index||!positive(item.version)||!duration(item.durationMs)
  ||!['not_run','running','succeeded','failed','unknown','skipped'].includes(item.state))throw invalid();
 for(const [state,total]of [['not_run',v.notRun],['running',v.running],['succeeded',v.succeeded],['failed',v.failed],['unknown',v.unknown],['skipped',v.skipped]]as const)
  if(!count(total)||v.cases.filter(x=>x.state===state).length!==total)throw invalid();
 const measured=v.cases.flatMap(x=>typeof x.durationMs==='number'?[x.durationMs]:[]).sort((a,b)=>a-b);
 if(v.measured!==measured.length||!duration(v.meanDurationMs)||!duration(v.p95DurationMs)
  ||(measured.length===0?v.meanDurationMs!==null||v.p95DurationMs!==null:v.meanDurationMs===null||v.p95DurationMs===null
   ||Math.abs(v.meanDurationMs-measured.reduce((a,b)=>a+b,0)/measured.length)>.001||v.p95DurationMs!==measured[Math.ceil(measured.length*.95)-1]))throw invalid();
 return v;
}
/** 实际输出和人工判断分开；账本未知费用不可用零伪造，未获财务权不接受财务字段。 */
export function validateEvaluationCase(input:AiKnowledgeEvaluationCaseResult,strategy:AiKnowledgeEvaluationStrategy,caseId:string):AiKnowledgeEvaluationCaseResult{
 const v=readAiKnowledgeEvaluationCaseResult(input);
 if(v.case.id!==caseId||!guid.test(v.case.sampleId)||!positive(v.case.version)||!duration(v.case.durationMs)||v.measurementProtocol!==protocol
  ||!['not_run','running','succeeded','failed','unknown','skipped'].includes(v.case.state))throw invalid();
 if(v.case.state==='succeeded'?v.baseline===null||v.candidate===null&&v.refinementRefused!==true:v.baseline!==null||v.candidate!==null)throw invalid();
 for(const rank of [v.baseline,v.candidate])if(rank!==null){if(rank.hits.length>5)throw invalid();const seen=new Set<string>();
  for(const hit of rank.hits){const key=hit.source.documentId+':'+hit.sequence;
   if(seen.has(key)||!isPublishedSearchHit(hit,strategy.sources)||!strategy.sources.some(x=>sameKnowledgeSource(x,hit.source))||!Number.isFinite(hit.score))throw invalid();seen.add(key);}}
 if(v.financeState==='not_authorized'){if(v.operations!==null)throw invalid();}
 else if(v.financeState!=='available'||v.operations===null||v.operations.length!==(strategy.candidate==='vector'?1:5)||new Set(v.operations.map(x=>x.id)).size!==v.operations.length)throw invalid();
 if(v.operations!==null)for(const operation of v.operations)if(!guid.test(operation.id)||!['known','unknown','reserved','not_recorded'].includes(operation.usageStatus)
  ||!amount(operation.recordedCost)||!amount(operation.reservedCost)||operation.usageStatus!=='known'&&operation.recordedCost!==null)throw invalid();
 return v;
}
/** 币种、原月份及价格分组原样保留，不提供跨币种总额或旧操作的猜测归属。 */
export function validateKnowledgeFinance(input:AiKnowledgeFinanceReport):AiKnowledgeFinanceReport{
 const v=readAiKnowledgeFinanceReport(input);const keys=new Set<string>();
 if(v.items.length>200||v.measurementState!=='ledger_latency_not_recorded'||Date.parse(v.fromUtc)>=Date.parse(v.toUtc))throw invalid();
 for(const item of v.items){const key=JSON.stringify([item.knowledgeBaseId,item.month,item.priceVersionId,item.currency]);
  if(keys.has(key)||![item.requests,item.known,item.unknown,item.reserved,item.missingCost].every(count)||item.known+item.unknown+item.reserved!==item.requests
   ||item.missingCost>item.requests||item.missingCost<item.unknown+item.reserved||!amount(item.recordedCost)||!amount(item.conservativeReservation)
   ||!/^\d{4}-\d{2}$/.test(item.month)||(item.knowledgeBaseId===null?item.attribution!=='unattributed':!guid.test(item.knowledgeBaseId)||item.attribution!=='explicit_operation'))throw invalid();keys.add(key);}
 return v;
}
/** 只有新观测的拒答才计数；旧答案没有持久标记，不能以零拒答声称历史覆盖。 */
export function validateKnowledgeQuality(input:AiKnowledgeQualityObservation,knowledge:string):AiKnowledgeQualityObservation{
 const v=readAiKnowledgeQualityObservation(input);
 if(v.knowledgeBaseId!==knowledge||![v.recordedAnswers,v.refused,v.measured,v.currentNegativeFeedback].every(count)||v.refused>v.recordedAnswers||v.measured>v.recordedAnswers
  ||!duration(v.meanPipelineMs)||(v.measured===0)!==(v.meanPipelineMs===null)||v.historicalCoverage!=='older_answers_not_recorded'||v.measurementProtocol!=='paid_knowledge_pipeline_monotonic_v1')throw invalid();return v;
}
