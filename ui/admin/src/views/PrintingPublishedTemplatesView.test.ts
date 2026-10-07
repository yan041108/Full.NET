import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import PrintingPublishedTemplatesView from './PrintingPublishedTemplatesView.vue';
import { listPrintingPublishedTemplates, previewPrintingPublishedTemplate, listPrintingTemplates } from '../api/printing-templates';
import { createOutputSession, deferred, outputId, printResult } from '../test/data-output-fixtures';
vi.mock('../api/printing-templates', () => ({listPrintingPublishedTemplates:vi.fn(),previewPrintingPublishedTemplate:vi.fn(),listPrintingTemplates:vi.fn()}));
const published = {templateId:outputId,templateKey:'fixture',templateName:'已授权模板',formSchemaKey:'printing.tenant_profile_card',versionNumber:1};
let wrapper: VueWrapper;
function create(permissions=['printing.published_templates.read','printing.published_templates.preview'], host=false) {
 const context=createOutputSession(permissions);
 if(host) context.session.currentUser={...context.session.currentUser!,scope:'host',actorScope:'host',tenantId:null};
 wrapper=mount(PrintingPublishedTemplatesView,{global:{plugins:[context.pinia]}});
 return context.session;
}
beforeEach(()=>{vi.resetAllMocks();vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([published]);vi.mocked(previewPrintingPublishedTemplate).mockResolvedValue(printResult);});
afterEach(()=>{wrapper?.unmount();vi.restoreAllMocks();});
describe('租户已授权打印目录',()=>{
 it('同模板切换版本取消旧响应，打印只重验当前精确版本',async()=>{
  vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([{...published,versionNumber:2},published]);
  create();await flushPromises();const pending=deferred<typeof printResult>();vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');const signal=vi.mocked(previewPrintingPublishedTemplate).mock.calls[0]![2]!;
  wrapper.getComponent(ElSelect).vm.$emit('update:modelValue',outputId+':1');wrapper.getComponent(ElSelect).vm.$emit('change');await flushPromises();
  pending.resolve({...printResult,versionNumber:2,html:'<div>旧版本迟到内容</div>'});await flushPromises();
  expect(signal.aborted).toBe(true);expect(wrapper.text()).not.toContain('旧版本迟到内容');
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  const print=vi.spyOn(window,'print').mockImplementation(()=>{});await wrapper.get('[data-testid="printing-published-print"]').trigger('click');await flushPromises();
  expect(previewPrintingPublishedTemplate).toHaveBeenLastCalledWith(outputId,{versionNumber:1},expect.any(AbortSignal));expect(print).toHaveBeenCalledOnce();
 });
 it('同一模板同时获授两个版本时可以选择旧版本并精确预览',async()=>{
  vi.mocked(listPrintingPublishedTemplates).mockResolvedValue([{...published,versionNumber:2},published]);
  create();await flushPromises();
  wrapper.getComponent(ElSelect).vm.$emit('update:modelValue',outputId+':1');wrapper.getComponent(ElSelect).vm.$emit('change');await flushPromises();
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  expect(previewPrintingPublishedTemplate).toHaveBeenCalledWith(outputId,{versionNumber:1},expect.any(AbortSignal));
 });
 it('只读取租户发布目录，显式指定目录中的冻结版本，不请求 Host 草稿',async()=>{
  create();await flushPromises();await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  expect(listPrintingTemplates).not.toHaveBeenCalled();
  expect(previewPrintingPublishedTemplate).toHaveBeenCalledWith(outputId,{versionNumber:1},expect.any(AbortSignal));
  expect(wrapper.text()).toContain('旧租户敏感内容');
 });
 it('只有读取权限时没有预览和打印入口',async()=>{
  create(['printing.published_templates.read']);await flushPromises();
  expect(wrapper.find('[data-testid="printing-published-preview"]').exists()).toBe(false);
  expect(wrapper.find('[data-testid="printing-published-print"]').exists()).toBe(false);
 });
 it('无读取权限或 Host 上下文均不读取发布目录',async()=>{
  create([]);await flushPromises();expect(listPrintingPublishedTemplates).not.toHaveBeenCalled();wrapper.unmount();
  create(undefined,true);await flushPromises();expect(listPrintingPublishedTemplates).not.toHaveBeenCalled();
 });
 it('打印前重验冻结版本，撤权后移除旧预览且不打印',async()=>{
  create();await flushPromises();await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  vi.mocked(previewPrintingPublishedTemplate).mockRejectedValueOnce({status:403,code:'authorization.permission_denied',title:'Denied'});
  const print=vi.spyOn(window,'print').mockImplementation(()=>{});
  await wrapper.get('[data-testid="printing-published-print"]').trigger('click');await flushPromises();
  expect(previewPrintingPublishedTemplate).toHaveBeenCalledTimes(2);expect(print).not.toHaveBeenCalled();
  expect(wrapper.text()).not.toContain('旧租户敏感内容');
 });
 it('打印等待服务器最新内容进入 DOM 后只打印一次',async()=>{
  create();await flushPromises();await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  const pending=deferred<typeof printResult>();vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
  const print=vi.spyOn(window,'print').mockImplementation(()=>{expect(wrapper.text()).toContain('最新内容');});
  const button=wrapper.get('[data-testid="printing-published-print"]');await button.trigger('click');await button.trigger('click');expect(print).not.toHaveBeenCalled();
  pending.resolve({...printResult,html:'<div>最新内容</div>'});await flushPromises();expect(print).toHaveBeenCalledOnce();
 });
 it('切换租户立即取消打印刷新，不恢复旧预览或调用 print',async()=>{
  const session=create();await flushPromises();await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  const pending=deferred<typeof printResult>();vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
  const print=vi.spyOn(window,'print').mockImplementation(()=>{});await wrapper.get('[data-testid="printing-published-print"]').trigger('click');
  const signal=vi.mocked(previewPrintingPublishedTemplate).mock.calls[1]![2]!;
  session.currentUser={...session.currentUser!,tenantId:'019bc2b1-2a40-7cc3-8992-a80de51bf298'};await flushPromises();
  pending.resolve(printResult);await flushPromises();expect(signal.aborted).toBe(true);expect(print).not.toHaveBeenCalled();expect(wrapper.text()).not.toContain('旧租户敏感内容');
 });
 it('换模板取消旧请求，并拒绝服务器返回其他模板或版本',async()=>{
  create();await flushPromises();const pending=deferred<typeof printResult>();vi.mocked(previewPrintingPublishedTemplate).mockReturnValueOnce(pending.promise);
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');
  wrapper.getComponent(ElSelect).vm.$emit('update:modelValue','unknown');wrapper.getComponent(ElSelect).vm.$emit('change');
  pending.resolve(printResult);await flushPromises();expect(wrapper.text()).not.toContain('旧租户敏感内容');
 });
 it.each([{templateId:'another'},{versionNumber:2}])('拒绝错误预览身份 %j',async(change)=>{
  create();await flushPromises();vi.mocked(previewPrintingPublishedTemplate).mockResolvedValueOnce({...printResult,...change});
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();expect(wrapper.text()).not.toContain('旧租户敏感内容');
 });
 it('DOM 插入前移除可执行布局，保留排版',async()=>{
  create();await flushPromises();vi.mocked(previewPrintingPublishedTemplate).mockResolvedValueOnce({...printResult,html:'<div style="color:red">安全正文</div><script>alert(1)</script><svg onload="alert(2)"></svg><input><img src="x" onerror="alert(3)">'});
  await wrapper.get('[data-testid="printing-published-preview"]').trigger('click');await flushPromises();
  const surface=wrapper.get('.printing-preview-html');expect(surface.text()).toContain('安全正文');expect(surface.find('script,svg,input,[onerror]').exists()).toBe(false);
  expect(surface.get('div').attributes('style')).toContain('color');
 });
});
