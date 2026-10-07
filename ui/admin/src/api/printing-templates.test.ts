import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate, listPrintingTemplateVersions, listPrintingTenantVersionGrants, setPrintingTenantVersionGrant } from './printing-templates';
import { outputId, printResult } from '../test/data-output-fixtures';
vi.mock('./http',()=>({http:{request:vi.fn(),requestBlob:vi.fn()},request:vi.fn()}));
const request=vi.mocked(http.request);
beforeEach(()=>vi.resetAllMocks());
describe('打印发布与授权生成 SDK 适配',()=>{
 it('使用生成目录操作并保留取消信号',async()=>{
  request.mockResolvedValue([{templateId:outputId,templateKey:'fixture',templateName:'Fixture',versionNumber:1,formSchemaKey:'fixture'}]);
  const signal=new AbortController().signal;expect(await listPrintingPublishedTemplates(signal)).toHaveLength(1);
  expect(request).toHaveBeenCalledWith('/api/v1/printing/published-templates',{method:'GET'},signal);
 });
 it('目录拒绝畸形响应',async()=>{request.mockResolvedValue([{templateId:'bad'}]);await expect(listPrintingPublishedTemplates()).rejects.toThrow();});
 it('精确版本预览保留生成守卫并校验绑定值',async()=>{
  request.mockResolvedValue(printResult);await previewPrintingPublishedTemplate(outputId,{versionNumber:1});
  expect(request).toHaveBeenCalledWith('/api/v1/printing/published-templates/'+outputId+'/preview',expect.objectContaining({method:'POST',body:'{"versionNumber":1}'}),undefined);
  request.mockResolvedValue({...printResult,boundFields:{secret:123}});await expect(previewPrintingPublishedTemplate(outputId)).rejects.toThrow();
 });
 it('Host 版本列表与分页授权走生成操作',async()=>{
  request.mockResolvedValue([]);expect(await listPrintingTemplateVersions(outputId)).toEqual([]);
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
