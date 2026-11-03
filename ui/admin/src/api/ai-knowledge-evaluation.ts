import { aiListKnowledgeEvaluationSets,aiGetKnowledgeEvaluationSet,aiCreateKnowledgeEvaluationSet,aiAppendKnowledgeEvaluationSet,
  aiRestoreKnowledgeEvaluationSet,aiCopyKnowledgeEvaluationSet,aiApproveKnowledgeEvaluationSet,aiWithdrawKnowledgeEvaluationSet,
  validateKnowledgeEvaluation,validateKnowledgeEvaluationPage,validateKnowledgeEvaluationMutation } from '@fullnet/client-contracts';
import {http} from './http';
import {aiPrepareKnowledgeEvaluationFeedback} from '@fullnet/client-contracts';

/** 旧候选在准确目标库内重新捕获原答案与完整来源，再获得本次保存专用凭据。 */
export async function prepareEvaluationFeedback(input:Parameters<typeof aiPrepareKnowledgeEvaluationFeedback>[1],signal?:AbortSignal){
  const value=await aiPrepareKnowledgeEvaluationFeedback(http,input,signal);
  if(!/^[0-9a-f]{64}$/.test(value.sourceHash)||!/^[0-9a-f]{64}$/.test(value.sampleHash)
    ||!Number.isFinite(Date.parse(value.expiresAtUtc))||value.persistenceToken.length===0||value.persistenceToken.length>384*1024)throw new Error('client.invalid_knowledge_evaluation');
  return value;
}

/** 评估输入与返回都走生成契约；短期候选只在原交互请求内使用，禁止放入存储或下载。 */
export async function listEvaluationSets(input:Parameters<typeof aiListKnowledgeEvaluationSets>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationPage(await aiListKnowledgeEvaluationSets(http,input,signal),input.knowledgeBaseId);
}
/** 读取正文前由服务端重新核对完整保留来源。 */
export async function getEvaluationSet(input:Parameters<typeof aiGetKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluation(await aiGetKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId,input.setId,input.revisionId);
}
/** 创建不等于批准或实际执行。 */
export async function createEvaluationSet(input:Parameters<typeof aiCreateKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiCreateKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
/** 编辑始终追加，不覆盖冻结修订。 */
export async function appendEvaluationSet(input:Parameters<typeof aiAppendKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiAppendKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
/** 恢复只复制正文，保留累计限制。 */
export async function restoreEvaluationSet(input:Parameters<typeof aiRestoreKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiRestoreKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
/** 同库复制仍保留来源与贡献者。 */
export async function copyEvaluationSet(input:Parameters<typeof aiCopyKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiCopyKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
/** 批准不会自动切换生产策略或调用模型。 */
export async function approveEvaluationSet(input:Parameters<typeof aiApproveKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiApproveKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
/** 撤回追加审核决定，冻结正文保持。 */
export async function withdrawEvaluationSet(input:Parameters<typeof aiWithdrawKnowledgeEvaluationSet>[1],signal?:AbortSignal){
  return validateKnowledgeEvaluationMutation(await aiWithdrawKnowledgeEvaluationSet(http,input,signal),input.knowledgeBaseId);
}
