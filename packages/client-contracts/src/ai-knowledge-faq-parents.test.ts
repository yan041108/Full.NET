import {describe,it,expect} from 'vitest';
import {validateKnowledgeFaqWorkspace,validateKnowledgeFaqSources} from './ai-knowledge-faq-drafts.js';
import {validateKnowledgeFaqTransferPackage} from './ai-knowledge-faq-transfer.js';
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const source=(n:number)=>({knowledgeBaseId:id(8),knowledgeBaseVersion:1,documentId:id(n),documentVersion:1,versionId:id(n+10),publicationId:id(n+20),generationId:id(n+30),manifestHash:'C'.repeat(64),sourceKind:'faq-revision-v1',faqId:id(n+40),faqRevisionId:id(n+50)});
const workspace=()=>{const parent=source(60);return{id:id(1),knowledgeBaseId:id(2),reservedDocumentId:id(3),state:'draft',origin:'faq_derived',basis:null,sourceHash:'A'.repeat(64),sources:[parent],headVersion:1,
  graph:{roots:[parent.documentId],nodes:[{source:parent,parentDocumentIds:[] as string[]}]},revision:{id:id(4),number:1,content:{question:'父 FAQ 派生问题',aliases:[],answer:'派生答案',category:'制度',validFromUtc:null,expiresAtUtc:null},contentHash:'B'.repeat(64),createdAtUtc:'2026-10-29T00:00:00Z',restoredFromRevisionId:null}};};
describe('真实 FAQ 父节点协议',()=>{
  it('独立父节点仍是父资格，不能变成无依赖的独立答案',()=>{const w=workspace();expect(validateKnowledgeFaqWorkspace(w,id(2)).graph?.roots).toEqual(w.graph.roots);expect(()=>validateKnowledgeFaqWorkspace({...w,origin:'independent',basis:'改写依据',sources:[],graph:undefined},id(2),w)).toThrow();});
  it.each(['missing_graph','leaf_downgrade','cycle','missing_parent','orphan','duplicate','empty_roots','legacy_format'])('拒绝图的降级、环或截断 %s',fault=>{const w=workspace();const pack={format:'faq-transfer-v4',items:[{faqId:w.id,knowledgeBaseId:w.knowledgeBaseId,revisionId:w.revision.id,headVersion:1,origin:w.origin,basis:null,sourceHash:w.sourceHash,contentHash:w.revision.contentHash,content:w.revision.content,sources:w.sources,graph:w.graph}]};
    const item=pack.items[0]!;
    if(fault==='missing_graph')Reflect.deleteProperty(item,'graph');if(fault==='leaf_downgrade')item.sources[0]!.sourceKind='document-v1';
    if(fault==='cycle')item.graph.nodes[0]!.parentDocumentIds=[item.graph.roots[0]!];if(fault==='missing_parent')item.graph.nodes[0]!.parentDocumentIds=[id(999)];
    if(fault==='orphan'){const extra=source(70);item.sources.push(extra);item.graph.nodes.push({source:extra,parentDocumentIds:[]});}
    if(fault==='duplicate')item.graph.nodes.push(item.graph.nodes[0]!);if(fault==='empty_roots')item.graph.roots=[];if(fault==='legacy_format')pack.format='faq-transfer-v1';
    expect(()=>validateKnowledgeFaqTransferPackage(pack)).toThrow();
  });
  it('导出再导入仍保留父发布、修订与边，不将旧格式当成兼容权威',()=>{const w=workspace();const item={faqId:w.id,knowledgeBaseId:w.knowledgeBaseId,revisionId:w.revision.id,headVersion:1,origin:w.origin,basis:null,sourceHash:w.sourceHash,contentHash:w.revision.contentHash,content:w.revision.content,sources:w.sources,graph:w.graph};expect(validateKnowledgeFaqTransferPackage({format:'faq-transfer-v4',items:[item]}).items[0]?.graph).toEqual(w.graph);});
  it('全部来源和父图共享八项总预算',()=>{const w=workspace();const sources=Array.from({length:9},(_,i)=>source(60+i));expect(()=>validateKnowledgeFaqSources(w.origin,null,sources,w.graph)).toThrow();});
});
