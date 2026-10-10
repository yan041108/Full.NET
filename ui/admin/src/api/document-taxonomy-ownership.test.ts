import { describe, beforeEach, it, expect, vi } from 'vitest';
import { http } from './http';
import { updateDocumentCategory } from './host-document-categories';
import { updateDocumentTag } from './host-document-tags';
vi.mock('./http',()=>({http:{request:vi.fn(),requestBlob:vi.fn()}}));
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299',other='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const base={id,name:'目录',code:null,icon:null,color:null,description:null,createdAtUtc:'2026-10-10T00:00:00Z',updatedAtUtc:null,version:1};
const configs=[{kind:'category',value:{...base,parentId:null,sortOrder:0},update:(signal?:AbortSignal)=>updateDocumentCategory(id,'目录',null,0,null,null,null,null,1,signal)}, {kind:'tag',value:{...base,useCount:0,isHot:false,isRecommended:false},update:(signal?:AbortSignal)=>updateDocumentTag(id,'目录',null,null,null,null,1,false,false,signal)}];
beforeEach(()=>vi.resetAllMocks());
for(const config of configs)describe(config.kind+'更新归属',()=>{
 it('拒绝其他对象合法响应',async()=>{vi.mocked(http.request).mockResolvedValue({...config.value,id:other});await expect(config.update()).rejects.toThrow(new RegExp('^client\\.invalid_document_'+config.kind+'_identity$'));});
 it('接受UUID大小写等价并透传取消信号',async()=>{vi.mocked(http.request).mockResolvedValue({...config.value,id:id.toUpperCase()});const signal=new AbortController().signal;await expect(config.update(signal)).resolves.toMatchObject({id:id.toUpperCase()});expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);});
});
