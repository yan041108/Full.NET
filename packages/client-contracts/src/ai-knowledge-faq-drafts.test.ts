import { describe, expect, it } from 'vitest';
import { validateKnowledgeFaqWorkspace, validateKnowledgeFaqMutation, validateKnowledgeFaqPage, validateKnowledgeFaqHistory } from './ai-knowledge-faq-drafts.js';
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const workspace=()=>({id:id(1),knowledgeBaseId:id(2),reservedDocumentId:id(3),state:'draft',origin:'independent',basis:'编写依据',sourceHash:'A'.repeat(64),sources:[],headVersion:1,
  revision:{id:id(4),number:1,content:{question:'问题',aliases:[],answer:'答案',category:'制度',validFromUtc:null,expiresAtUtc:null},contentHash:'B'.repeat(64),createdAtUtc:'2026-10-29T00:00:00Z',restoredFromRevisionId:null}});
const source={knowledgeBaseId:id(8),knowledgeBaseVersion:1,documentId:id(9),documentVersion:1,versionId:id(10),publicationId:id(11),generationId:id(12),manifestHash:'C'.repeat(64)};
describe('FAQ 原始来源与修订响应边界',()=>{
  it('日程完整身份不能经修订响应清空、替换或转换为独立来源',()=>{
    const personal={id:id(60),version:2,projectionHash:'D'.repeat(64)};
    const before={...workspace(),origin:'document_derived',basis:null,sources:[source],personalSchedules:[personal]};
    expect(validateKnowledgeFaqWorkspace(before,id(2)).personalSchedules).toEqual([personal]);
    expect(()=>validateKnowledgeFaqWorkspace({...before,personalSchedules:undefined},id(2),before)).toThrow();
    expect(()=>validateKnowledgeFaqWorkspace({...before,personalSchedules:[{...personal,version:3}]},id(2),before)).toThrow();
    expect(()=>validateKnowledgeFaqWorkspace({...workspace(),personalSchedules:[personal]},id(2))).toThrow();
  });
  it('接受尚未发布的独立草稿',()=>expect(validateKnowledgeFaqWorkspace(workspace(),id(2)).basis).toBe('编写依据'));
  it.each(['base','head','state','basis','reserved','alias','date'])('拒绝错配 %s',fault=>{const w=workspace();if(fault==='base')w.knowledgeBaseId=id(90);if(fault==='head')w.headVersion=2;if(fault==='state')w.state='published';if(fault==='basis')w.basis='';if(fault==='reserved')w.reservedDocumentId=w.id;if(fault==='alias')w.revision.content.aliases=Array(17).fill('问') as never[];if(fault==='date')w.revision.createdAtUtc='?';expect(()=>validateKnowledgeFaqWorkspace(w,id(2))).toThrow();});
  it('派生编辑不能降级或删除原始依赖',()=>{const before={...workspace(),origin:'document_derived',basis:null,sources:[source]};expect(validateKnowledgeFaqWorkspace(before,id(2))).toEqual(before);expect(()=>validateKnowledgeFaqWorkspace(workspace(),id(2),before)).toThrow();expect(()=>validateKnowledgeFaqWorkspace({...before,sources:[]},id(2),before)).toThrow();});
  it('不同源发布即使来源数量相同也拒绝',()=>{const before={...workspace(),origin:'document_derived',basis:null,sources:[source]};expect(()=>validateKnowledgeFaqWorkspace({...before,sources:[{...source,publicationId:id(99)}]},id(2),before)).toThrow();});
  it('旧请求重放可以返回当前头，新保存必须绑定当前修订',()=>{const w=workspace();expect(validateKnowledgeFaqMutation({workspace:w,savedRevisionId:id(7),replayed:true},id(2)).replayed).toBe(true);expect(()=>validateKnowledgeFaqMutation({workspace:w,savedRevisionId:id(7),replayed:false},id(2))).toThrow();});
  it('页面不接受重复身份或循环游标',()=>{const w=workspace();expect(()=>validateKnowledgeFaqPage({items:[w,w],nextAfterId:null},id(2))).toThrow();expect(()=>validateKnowledgeFaqPage({items:[],nextAfterId:id(7)},id(2),id(7))).toThrow();});
  it('历史修订严格递减且不能超过头',()=>{const w=workspace();expect(validateKnowledgeFaqHistory({items:[w.revision],nextAfterId:null},w).items).toHaveLength(1);expect(()=>validateKnowledgeFaqHistory({items:[w.revision,w.revision],nextAfterId:null},w)).toThrow();});
});
