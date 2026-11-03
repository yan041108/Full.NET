import {aiGetKnowledgeBase,aiListKnowledgeEvaluationStrategies,aiCaptureKnowledgeEvaluationStrategy,aiGetKnowledgeEvaluationStrategy,
 aiPreviewKnowledgeEvaluationRun,aiCreateKnowledgeEvaluationRun,aiListKnowledgeEvaluationRuns,aiGetKnowledgeEvaluationRun,
 aiGetKnowledgeEvaluationCase,aiExecuteKnowledgeEvaluationCase,aiSkipKnowledgeEvaluationCase,aiCloseUnknownKnowledgeEvaluationCase,
 aiReviewKnowledgeEvaluationCase,aiGetKnowledgeFinance,aiGetKnowledgeQualityObservations,readAiKnowledgeBaseResponse,
 validateEvaluationStrategy,validateEvaluationAdmission,validateEvaluationRun,validateEvaluationCase,validateKnowledgeFinance,validateKnowledgeQuality,
 type AiKnowledgeEvaluationStrategy} from '@fullnet/client-contracts';
import {http} from './http';
import {searchKnowledge} from './ai-knowledge-search';

/** UUID只用于本次明确操作的原请求身份；网络不确定时保留并重放同一身份。 */
export function evaluationIdentity(){const bytes=crypto.getRandomValues(new Uint8Array(16));let now=BigInt(Date.now());for(let i=5;i>=0;i--){bytes[i]=Number(now&255n);now>>=8n;}bytes[6]=(bytes[6]!&15)|112;bytes[8]=(bytes[8]!&63)|128;const hex=Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;}
/** 无模型调用的完整候选捕获；不能把排名第一项当作整组冻结来源。 */
export async function prepareEvaluationStrategy(knowledgeBaseId:string,question:string,signal?:AbortSignal,sourceKnowledgeBaseId=knowledgeBaseId){
 const base=readAiKnowledgeBaseResponse(await aiGetKnowledgeBase(http,{knowledgeBaseId},signal));
 if(!base.isEnabled||!base.embeddingModelConfigId||!base.embeddingModelVersion)throw new Error('client.invalid_knowledge_evaluation_run');
 const result=await searchKnowledge({body:{knowledgeBaseIds:[sourceKnowledgeBaseId],documentIds:[],embeddingModelConfigId:base.embeddingModelConfigId,
  embeddingModelVersion:base.embeddingModelVersion,query:question,limit:1,includeDiagnostics:false,expectedSources:null}},signal);
 return {base,sources:result.sources};
}
/** 目录只含身份；展开正文仍走完整来源核证。 */
export const listEvaluationStrategies=(p:Parameters<typeof aiListKnowledgeEvaluationStrategies>[1],signal?:AbortSignal)=>aiListKnowledgeEvaluationStrategies(http,p,signal);
/** 捕获不自动执行或替换生产策略。 */
export async function captureEvaluationStrategy(p:Parameters<typeof aiCaptureKnowledgeEvaluationStrategy>[1],signal?:AbortSignal){return validateEvaluationStrategy(await aiCaptureKnowledgeEvaluationStrategy(http,p,signal),p.knowledgeBaseId);}
/** 精确策略版本的当前来源复核。 */
export async function getEvaluationStrategy(p:Parameters<typeof aiGetKnowledgeEvaluationStrategy>[1],signal?:AbortSignal){return validateEvaluationStrategy(await aiGetKnowledgeEvaluationStrategy(http,p,signal),p.knowledgeBaseId);}
/** 预览无收费；金额由后续原账本反映，不伪造固定报价。 */
export async function previewEvaluationRun(p:Parameters<typeof aiPreviewKnowledgeEvaluationRun>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationAdmission(await aiPreviewKnowledgeEvaluationRun(http,p,signal),strategy);}
/** 明确确认只创建原计划，各样例仍需实际领取执行。 */
export async function createEvaluationRun(p:Parameters<typeof aiCreateKnowledgeEvaluationRun>[1],signal?:AbortSignal){const v=validateEvaluationRun(await aiCreateKnowledgeEvaluationRun(http,p,signal),p.knowledgeBaseId);if(v.strategyId!==p.strategyId||v.strategyRevisionId!==p.body.strategyRevisionId)throw new Error('client.invalid_knowledge_evaluation_run');return v;}
/** 元数据目录不提升私人正文读取权。 */
export const listEvaluationRuns=(p:Parameters<typeof aiListKnowledgeEvaluationRuns>[1],signal?:AbortSignal)=>aiListKnowledgeEvaluationRuns(http,p,signal);
/** 全状态分母与真实测量保持。 */
export async function getEvaluationRun(p:Parameters<typeof aiGetKnowledgeEvaluationRun>[1],signal?:AbortSignal){return validateEvaluationRun(await aiGetKnowledgeEvaluationRun(http,p,signal),p.knowledgeBaseId,p.runId);}
/** 只有准确策略来源中的结果可以展示。 */
export async function getEvaluationCase(p:Parameters<typeof aiGetKnowledgeEvaluationCase>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationCase(await aiGetKnowledgeEvaluationCase(http,p,signal),strategy,p.caseId);}
/** 一次明确执行，不在客户端自动重试。 */
export async function executeEvaluationCase(p:Parameters<typeof aiExecuteKnowledgeEvaluationCase>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationCase(await aiExecuteKnowledgeEvaluationCase(http,p,signal),strategy,p.caseId);}
/** 跳过仍在分母中。 */
export async function skipEvaluationCase(p:Parameters<typeof aiSkipKnowledgeEvaluationCase>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationCase(await aiSkipKnowledgeEvaluationCase(http,p,signal),strategy,p.caseId);}
/** 关闭未知不会重新派发模型。 */
export async function closeUnknownEvaluationCase(p:Parameters<typeof aiCloseUnknownKnowledgeEvaluationCase>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationCase(await aiCloseUnknownKnowledgeEvaluationCase(http,p,signal),strategy,p.caseId);}
/** 人工支持判断独立于模型输出。 */
export async function reviewEvaluationCase(p:Parameters<typeof aiReviewKnowledgeEvaluationCase>[1],strategy:AiKnowledgeEvaluationStrategy,signal?:AbortSignal){return validateEvaluationCase(await aiReviewKnowledgeEvaluationCase(http,p,signal),strategy,p.caseId);}
/** 独立精确财务权限，分组与空金额原样保留。 */
export async function getKnowledgeFinance(p:Parameters<typeof aiGetKnowledgeFinance>[1],signal?:AbortSignal){return validateKnowledgeFinance(await aiGetKnowledgeFinance(http,p,signal));}
/** 质量聚合不下载生产问题或答案。 */
export async function getKnowledgeQuality(p:Parameters<typeof aiGetKnowledgeQualityObservations>[1],signal?:AbortSignal){return validateKnowledgeQuality(await aiGetKnowledgeQualityObservations(http,p,signal),p.knowledgeBaseId);}
