import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import DocumentPublicShareView from './DocumentPublicShareView.vue';
import * as api from '../api/document-shares';
import { openDocumentBlob } from '../api/host-document-items';
import { deferred, outputId } from '../test/data-output-fixtures';
import { documentPreviewTask as task } from '../test/task-lifecycle-fixtures';
const routing=vi.hoisted(()=>({ route: undefined as unknown }));
vi.mock('vue-router',()=>({ useRoute:()=>routing.route }));
vi.mock('../api/document-shares',()=>({ accessDocumentShareByCode:vi.fn(),createDocumentSharePreviewTaskByCode:vi.fn(),getDocumentSharePreviewTaskByCode:vi.fn(),loadDocumentShareContentByCode:vi.fn(),loadDocumentSharePreviewTaskContentByCode:vi.fn() }));
vi.mock('../api/host-document-items',()=>({openDocumentBlob:vi.fn()}));
let route: {params:{shareCode:string}};let wrapper:VueWrapper|undefined;
const access={shareId:outputId,documentId:outputId,shareCode:'OLD',title:'旧分享敏感文档',fileName:'spec.docx',mimeType:'application/vnd.openxmlformats-officedocument.wordprocessingml.document',fileSizeBytes:12,hasPassword:false,accessCountRemaining:9};
beforeEach(()=>{vi.resetAllMocks();route=reactive({params:{shareCode:'OLD'}});routing.route=route;
 vi.mocked(api.accessDocumentShareByCode).mockResolvedValue(access);vi.mocked(api.createDocumentSharePreviewTaskByCode).mockResolvedValue(task);
 vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue(task);vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mockResolvedValue(new Blob(['pdf']));
 vi.mocked(api.loadDocumentShareContentByCode).mockResolvedValue(new Blob(['source']));vi.spyOn(URL,'createObjectURL').mockReturnValue('blob:preview');vi.spyOn(URL,'revokeObjectURL').mockImplementation(()=>{});
});
afterEach(()=>{wrapper?.unmount();wrapper=undefined;vi.useRealTimers();vi.restoreAllMocks();});
const start=async()=>{wrapper=mount(DocumentPublicShareView);await flushPromises();};
describe('公开文档分享请求与轮询归属',()=>{
 it.each(['resolve','reject'] as const)('切换分享码取消旧访问，迟到%s不接入新分享',async outcome=>{
  const pending=deferred<typeof access>();vi.mocked(api.accessDocumentShareByCode).mockReturnValueOnce(pending.promise).mockResolvedValueOnce({...access,shareCode:'NEW',title:'新分享'});
  await start();const signal=vi.mocked(api.accessDocumentShareByCode).mock.calls[0]?.[2];route.params.shareCode='NEW';
  expect(signal?.aborted).toBe(true);await flushPromises();
  if(outcome==='resolve')pending.resolve(access);else pending.reject(new Error('old'));await flushPromises();
  expect(api.accessDocumentShareByCode).toHaveBeenCalledTimes(2);expect(wrapper!.text()).toContain('新分享');expect(wrapper!.text()).not.toContain('旧分享敏感文档');
 });
 it('切换分享码清理已有预览、取消旧内容，晚到Blob不创建URL',async()=>{
  const pending=deferred<Blob>();vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(new Blob(['new']));
  vi.mocked(api.accessDocumentShareByCode).mockResolvedValueOnce(access).mockResolvedValueOnce({...access,shareCode:'NEW',title:'新分享'});
  await start();const signal=vi.mocked(api.loadDocumentSharePreviewTaskContentByCode).mock.calls[0]?.[3];route.params.shareCode='NEW';expect(signal?.aborted).toBe(true);await flushPromises();
  pending.resolve(new Blob(['old']));await flushPromises();expect(URL.createObjectURL).toHaveBeenCalledOnce();expect(wrapper!.text()).toContain('新分享');
 });
 it.each([{documentItemId:'019bc2b1-2a40-7cc3-8992-a80de51bf298'},{versionId:outputId}])('创建预览文档或版本错配不轮询 %j',async change=>{
  vi.mocked(api.createDocumentSharePreviewTaskByCode).mockResolvedValue({...task,...change});await start();
  expect(api.getDocumentSharePreviewTaskByCode).not.toHaveBeenCalled();expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled();expect(wrapper!.find('.el-alert').exists()).toBe(true);
 });
 it.each([{documentItemId:'019bc2b1-2a40-7cc3-8992-a80de51bf298'},{versionId:outputId},{sourceFileId:'019bc2b1-2a40-7cc3-8992-a80de51bf298'}])('轮询文档/版本/源文件错配不读取输出 %j',async change=>{
  vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue({...task,...change});await start();expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled();
 });
 it('卸载取消等待间隔并停止后续状态请求',async()=>{
  vi.useFakeTimers();vi.mocked(api.getDocumentSharePreviewTaskByCode).mockResolvedValue({...task,statusKey:'pending'});await start();
  const signal=vi.mocked(api.getDocumentSharePreviewTaskByCode).mock.calls[0]?.[3];wrapper!.unmount();wrapper=undefined;expect(signal?.aborted).toBe(true);
  await vi.advanceTimersByTimeAsync(60000);await flushPromises();expect(api.getDocumentSharePreviewTaskByCode).toHaveBeenCalledOnce();expect(api.loadDocumentSharePreviewTaskContentByCode).not.toHaveBeenCalled();
 });
 it('下载互斥，卸载后迟到下载不能打开敏感内容',async()=>{
  await start();const pending=deferred<Blob>();vi.mocked(api.loadDocumentShareContentByCode).mockReturnValue(pending.promise);
  const button=wrapper!.get('[data-testid="document-public-share-download"]');await button.trigger('click');await button.trigger('click');expect(api.loadDocumentShareContentByCode).toHaveBeenCalledOnce();
  const signal=vi.mocked(api.loadDocumentShareContentByCode).mock.calls[0]?.[2];wrapper!.unmount();wrapper=undefined;expect(signal?.aborted).toBe(true);pending.resolve(new Blob(['private']));await flushPromises();expect(openDocumentBlob).not.toHaveBeenCalled();
 });
 it('非ProblemDetails访问失败仍显示可重试错误',async()=>{
  vi.mocked(api.accessDocumentShareByCode).mockRejectedValueOnce(new Error('transport')).mockResolvedValueOnce(access);await start();
  expect(wrapper!.find('.el-alert').exists()).toBe(true);await wrapper!.get('[data-testid="document-public-share-submit"]').trigger('click');await flushPromises();expect(wrapper!.text()).toContain(access.title);
 });
});

describe('公开分享密码重试',()=>{
 it('要求密码后输错仍可改正，并使用本次访问密码读取内容',async()=>{
  vi.mocked(api.accessDocumentShareByCode).mockRejectedValueOnce({status:400,code:'document.host_share.password_required',title:'需要密码'})
   .mockRejectedValueOnce({status:403,code:'document.host_share.access_denied',title:'拒绝访问'}).mockResolvedValueOnce({...access,hasPassword:true});
  await start();await wrapper!.get('[data-testid="document-public-share-password"]').setValue('wrong');
  await wrapper!.get('[data-testid="document-public-share-submit"]').trigger('click');await flushPromises();
  expect(wrapper!.find('[data-testid="document-public-share-password"]').exists()).toBe(true);expect(wrapper!.text()).toContain('拒绝访问');
  await wrapper!.get('[data-testid="document-public-share-password"]').setValue('correct');
  await wrapper!.get('[data-testid="document-public-share-submit"]').trigger('click');await flushPromises();
  expect(api.accessDocumentShareByCode).toHaveBeenNthCalledWith(3,'OLD',{password:'correct'},expect.any(AbortSignal));
  expect(api.createDocumentSharePreviewTaskByCode).toHaveBeenCalledWith('OLD',{password:'correct'},expect.any(AbortSignal));
  expect(wrapper!.text()).toContain(access.title);
 });
});
