import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import PrintingPreviewView from './PrintingPreviewView.vue';
import { createPrintingTemplate, listPrintingTemplates, previewPrintingTemplate, publishPrintingTemplate } from '../api/printing-templates';
import { createOutputSession, deferred, printResult, printTemplate } from '../test/data-output-fixtures';
vi.mock('../api/printing-templates', () => ({ createPrintingTemplate: vi.fn(), listPrintingTemplates: vi.fn(), previewPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn() }));
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listPrintingTemplates).mockResolvedValue([printTemplate]); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const mountView = (permissions: string[]) => { const context = createOutputSession(permissions); context.session.currentUser = {...context.session.currentUser!,scope:'host',actorScope:'host',tenantId:null}; wrapper = mount(PrintingPreviewView, { global: { plugins: [context.pinia] } }); return context.session; };
describe('打印输出授权与生命周期', () => {
  it('预览失败可重试，已展示内容在注销时立即清理', async () => {
    vi.mocked(previewPrintingTemplate).mockRejectedValueOnce(new Error('failed')).mockResolvedValueOnce(printResult);
    const session = mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    expect(wrapper!.text()).toContain('旧租户敏感内容'); expect(previewPrintingTemplate).toHaveBeenCalledTimes(2);
    session.state = 'anonymous'; await flushPromises(); expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
  it('创建后撤销权限不能继续自动发布或预览', async () => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(createPrintingTemplate).mockReturnValue(pending.promise);
    const session = mountView(['printing.templates.read', 'printing.templates.create', 'printing.templates.publish', 'printing.templates.preview']);
    await flushPromises(); await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-submit"]').trigger('click');
    session.currentUser!.permissions = ['printing.templates.read']; pending.resolve(printTemplate); await flushPromises();
    expect(publishPrintingTemplate).not.toHaveBeenCalled(); expect(previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('无预览权限不创建操作入口，无读取权限不请求目录', async () => {
    mountView([]); await flushPromises(); expect(wrapper!.find('[data-testid="printing-preview-run"]').exists()).toBe(false); expect(listPrintingTemplates).not.toHaveBeenCalled();
  });
  it('预览撤权后取消请求且迟到结果不恢复敏感内容', async () => {
    const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingTemplate).mockReturnValue(pending.promise);
    const session = mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); const signal = vi.mocked(previewPrintingTemplate).mock.calls[0]?.[2];
    session.currentUser!.permissions = ['printing.templates.read']; await flushPromises(); pending.resolve(printResult); await flushPromises();
    expect(signal?.aborted).toBe(true); expect(wrapper!.text()).not.toContain('旧租户敏感内容'); expect(wrapper!.find('[data-testid="printing-preview-print"]').exists()).toBe(false);
  });
  it('选择另一模板取消旧预览，不把旧结果当作新模板', async () => {
    const pending = deferred<typeof printResult>(); vi.mocked(previewPrintingTemplate).mockReturnValue(pending.promise);
    mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises(); await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click');
    wrapper!.findComponent(ElSelect).vm.$emit('update:modelValue', 'new-template'); wrapper!.findComponent(ElSelect).vm.$emit('change', 'new-template');
    await flushPromises(); pending.resolve(printResult); await flushPromises(); expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
  it('已打开创建弹窗撤权后移除提交入口', async () => {
    const session = mountView(['printing.templates.read', 'printing.templates.create']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises();
    session.currentUser!.permissions = ['printing.templates.read']; await flushPromises();
    expect(wrapper!.find('[data-testid="printing-preview-submit"]').exists()).toBe(false); expect(createPrintingTemplate).not.toHaveBeenCalled();
  });
  it('创建权限不隐式授予发布或预览，重复点击只创建一次', async () => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(createPrintingTemplate).mockReturnValue(pending.promise);
    mountView(['printing.templates.read', 'printing.templates.create']); await flushPromises(); await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises();
    const button = wrapper!.get('[data-testid="printing-preview-submit"]'); await button.trigger('click'); await button.trigger('click'); pending.resolve(printTemplate); await flushPromises();
    expect(createPrintingTemplate).toHaveBeenCalledTimes(1); expect(publishPrintingTemplate).not.toHaveBeenCalled(); expect(previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('打印前重新获取预览，服务端撤权时立即清除旧内容且不调用浏览器打印', async () => {
    vi.mocked(previewPrintingTemplate).mockResolvedValueOnce(printResult).mockRejectedValueOnce({status:403,code:'authorization.permission_denied',title:'Denied'});
    const print = vi.spyOn(window,'print').mockImplementation(() => {});
    mountView(['printing.templates.read','printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    expect(wrapper!.text()).toContain('旧租户敏感内容');
    await wrapper!.get('[data-testid="printing-preview-print"]').trigger('click'); await flushPromises();
    expect(previewPrintingTemplate).toHaveBeenCalledTimes(2);
    expect(print).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });

});
