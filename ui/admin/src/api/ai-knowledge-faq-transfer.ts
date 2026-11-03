import { aiExportKnowledgeFaqs,aiPreviewKnowledgeFaqImport,aiImportKnowledgeFaqs,
  validateKnowledgeFaqTransferPackage,validateKnowledgeFaqImportReport,
  type AiExportKnowledgeFaqsParameters,type AiPreviewKnowledgeFaqImportParameters,type AiImportKnowledgeFaqsParameters } from '@fullnet/client-contracts';
import {http} from './http';
export async function exportFaqs(p:AiExportKnowledgeFaqsParameters,signal?:AbortSignal){
  const value=validateKnowledgeFaqTransferPackage(await aiExportKnowledgeFaqs(http,p,signal));
  if(value.items.length!==p.body.faqIds.length||value.items.some((item,i)=>item.faqId!==p.body.faqIds[i]||item.knowledgeBaseId!==p.knowledgeBaseId))throw new Error('client.invalid_knowledge_faq_transfer');
  return value;
}
export async function previewFaqImport(p:AiPreviewKnowledgeFaqImportParameters,signal?:AbortSignal){return validateKnowledgeFaqImportReport(await aiPreviewKnowledgeFaqImport(http,p,signal),p.body.package.items.length,false);}
export async function importFaqs(p:AiImportKnowledgeFaqsParameters,signal?:AbortSignal){
  const value=validateKnowledgeFaqImportReport(await aiImportKnowledgeFaqs(http,p,signal),p.body.package.items.length,true);
  if(value.digest!==p.body.expectedDigest)throw new Error('client.invalid_knowledge_faq_transfer');return value;
}
