import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate, listPrintingTemplateVersions, listPrintingTenantVersionGrants, setPrintingTenantVersionGrant } from './printing-templates';
import { outputId, printResult } from '../test/data-output-fixtures';
import {listPrintingFormSchemas,getPrintingFormSchema} from './printing-templates';
vi.mock('./http',()=>({http:{request:vi.fn(),requestBlob:vi.fn()},request:vi.fn()}));
const request=vi.mocked(http.request);
beforeEach(()=>vi.resetAllMocks());
const businessSchema={formSchemaKey:'demo.enterprise_request.summary',displayName:'Business summary',description:'Fixed business fields',requiresRecordId:true,fields:[{fieldKey:'requestNumber',displayName:'Number'}]};
describe('打印绑定表单目录',()=>{
 it('读取有界已注册表单，保留取消信号和业务记录资格',async()=>{request.mockResolvedValue([businessSchema]);const signal=new AbortController().signal;expect(await listPrintingFormSchemas(signal)).toEqual([businessSchema]);expect(request).toHaveBeenCalledExactlyOnceWith('/api/v1/printing/form-schemas',{method:'GET'},signal);});
 it.each([[businessSchema,businessSchema],[{...businessSchema,formSchemaKey:'bad\nschema'}],[{...businessSchema,requiresRecordId:'yes'}],[{...businessSchema,fields:[{fieldKey:'a.b',displayName:'Bad'}]}],[{...businessSchema,fields:[...businessSchema.fields,...businessSchema.fields]}],Array.from({length:65},(_,index)=>({...businessSchema,formSchemaKey:'schema.'+index}))].map(rows=>({rows})))('重复、不安全或无界表单目录拒绝接纳 %j',async({rows})=>{request.mockResolvedValue(rows);await expect(listPrintingFormSchemas()).rejects.toThrow();});
 it('单个字段目录继续绑定精确 Schema，并拒绝无界字段',async()=>{request.mockResolvedValue({...businessSchema,formSchemaKey:'other'});await expect(getPrintingFormSchema(businessSchema.formSchemaKey)).rejects.toThrow();request.mockResolvedValue({...businessSchema,fields:Array.from({length:65},(_,index)=>({fieldKey:'field'+index,displayName:'Field'}))});await expect(getPrintingFormSchema(businessSchema.formSchemaKey)).rejects.toThrow();});
});
describe('打印发布与授权生成 SDK 适配',()=>{
 it('使用生成目录操作并保留取消信号',async()=>{
  request.mockResolvedValue([{templateId:outputId,templateKey:'fixture',templateName:'Fixture',versionNumber:1,formSchemaKey:'fixture'}]);
  const signal=new AbortController().signal;expect(await listPrintingPublishedTemplates(signal)).toHaveLength(1);
  expect(request).toHaveBeenCalledWith('/api/v1/printing/published-templates',{method:'GET'},signal);
 });
 it('目录拒绝畸形响应',async()=>{request.mockResolvedValue([{templateId:'bad'}]);await expect(listPrintingPublishedTemplates()).rejects.toThrow();});
 it('精确版本预览保留生成守卫并校验绑定值',async()=>{
  request.mockResolvedValue(printResult);await previewPrintingPublishedTemplate(outputId,{versionNumber:1});
  expect(request).toHaveBeenCalledWith('/api/v1/printing/published-templates/'+outputId+'/preview',expect.objectContaining({method:'POST',body:'{"versionNumber":1,"recordId":null}'}),undefined);
  request.mockResolvedValue({...printResult,boundFields:{secret:123}});await expect(previewPrintingPublishedTemplate(outputId)).rejects.toThrow();
 });
 it('业务预览按生成契约传递记录编号和取消信号',async()=>{
  request.mockResolvedValue(printResult);const signal=new AbortController().signal;
  await previewPrintingPublishedTemplate(outputId,{versionNumber:1,recordId:outputId},signal);
  expect(request).toHaveBeenLastCalledWith('/api/v1/printing/published-templates/'+outputId+'/preview',expect.objectContaining({body:JSON.stringify({versionNumber:1,recordId:outputId})}),signal);
 });
 it('Host 版本列表与分页授权走生成操作',async()=>{
  const versions={items:[{id:outputId,templateId:outputId,versionNumber:1,changeNote:null,publishedByUserId:outputId,publishedAtUtc:'2026-11-05T00:00:00Z'}],page:1,pageSize:20,total:1};
  request.mockResolvedValue(versions);expect(await listPrintingTemplateVersions(outputId)).toEqual(versions);
  request.mockResolvedValue({items:[outputId],page:2,pageSize:20,total:21});
  expect((await listPrintingTenantVersionGrants(outputId,1,2)).page).toBe(2);
  expect(request).toHaveBeenLastCalledWith('/api/v1/printing/templates/'+outputId+'/versions/1/tenant-grants?page=2&pageSize=20',{method:'GET'},undefined);
 });
 it('授予和撤销同一精确版本，false 不能冒充成功',async()=>{
  request.mockResolvedValue(true);await setPrintingTenantVersionGrant(outputId,1,outputId,true);
  expect(request).toHaveBeenLastCalledWith('/api/v1/printing/templates/'+outputId+'/versions/1/tenant-grants/'+outputId,{method:'PUT'},undefined);
  await setPrintingTenantVersionGrant(outputId,1,outputId,false);
  expect(request).toHaveBeenLastCalledWith('/api/v1/printing/templates/'+outputId+'/versions/1/tenant-grants/'+outputId,{method:'DELETE'},undefined);
  request.mockResolvedValue(false);await expect(setPrintingTenantVersionGrant(outputId,1,outputId,true)).rejects.toThrow();
 });
});
