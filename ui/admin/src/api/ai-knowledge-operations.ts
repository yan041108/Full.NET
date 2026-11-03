import { aiListKnowledgeOperations,validateKnowledgeOperations,type AiListKnowledgeOperationsParameters } from '@fullnet/client-contracts';
import {http} from './http';

/** 当前维护者的无正文投影；未知或不匹配的响应不进入页面缓冲。 */
export async function listKnowledgeOperations(input:AiListKnowledgeOperationsParameters={},signal?:AbortSignal){
  return validateKnowledgeOperations(await aiListKnowledgeOperations(http,input,signal),input);
}
