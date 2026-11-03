import { aiSearchKnowledge, validateKnowledgeSearch, type AiSearchKnowledgeParameters } from '@fullnet/client-contracts';
import { aiReadKnowledgeSearchContext, validateKnowledgeSearchContext, type AiReadKnowledgeSearchContextParameters } from '@fullnet/client-contracts';
import { http } from './http';
import { aiCompareKnowledgeSearch, validateKnowledgeSearchComparison, type AiCompareKnowledgeSearchParameters } from '@fullnet/client-contracts';
import { aiReadKnowledgeSearchRelations, validateKnowledgeSearchRelations, type AiReadKnowledgeSearchRelationsParameters } from '@fullnet/client-contracts';
import { aiRefineKnowledgeSearch, validateKnowledgeSearchRefinement, type AiRefineKnowledgeSearchParameters } from '@fullnet/client-contracts';

/** 显式实验逐阶段计费，仍绑定原查询和完整来源；失败不自动重试或替换生产结果。 */
export async function refineKnowledgeSearch(parameters: AiRefineKnowledgeSearchParameters, signal?: AbortSignal) {
  return validateKnowledgeSearchRefinement(await aiRefineKnowledgeSearch(http, parameters, signal), parameters.body);
}

/** 关系及双方正文必须绑定同一完整发布；不把补写或邻接序号作为父子关系。 */
export async function readKnowledgeSearchRelations(parameters: AiReadKnowledgeSearchRelationsParameters, signal?: AbortSignal) {
  return validateKnowledgeSearchRelations(await aiReadKnowledgeSearchRelations(http, parameters, signal), parameters.body);
}

/** 显式收费比较只接受同一冻结选择；不复用失效排名或自动重试模型调用。 */
export async function compareKnowledgeSearch(parameters: AiCompareKnowledgeSearchParameters, signal?: AbortSignal) {
  return validateKnowledgeSearchComparison(await aiCompareKnowledgeSearch(http, parameters, signal), parameters.body);
}

/** 展示前再次绑定本次选择及版本身份；不能用迟到或混合来源结果替代当前搜索。 */
export async function searchKnowledge(parameters: AiSearchKnowledgeParameters, signal?: AbortSignal) {
  return validateKnowledgeSearch(await aiSearchKnowledge(http, parameters, signal), parameters.body);
}

/** 发布身份必须与原命中完全相同，未知或迟到窗口不能进入展示。 */
export async function readKnowledgeSearchContext(parameters: AiReadKnowledgeSearchContextParameters, signal?: AbortSignal) {
  return validateKnowledgeSearchContext(await aiReadKnowledgeSearchContext(http, parameters, signal), parameters.body);
}
