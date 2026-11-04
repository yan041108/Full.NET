import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import PrintingPreviewView from './PrintingPreviewView.vue';
import { createPrintingTemplate, listPrintingTemplates, previewPrintingTemplate, publishPrintingTemplate, getPrintingTemplateVersion, getPrintingTemplate, listPrintingTemplateVersions } from '../api/printing-templates';
import { createOutputSession, deferred, printTemplate } from '../test/data-output-fixtures';
import { hostPrintingDraft, hostPrintingVersion } from '../test/printing-host-fixtures';
vi.mock('../api/printing-templates', () => ({ createPrintingTemplate: vi.fn(), listPrintingTemplates: vi.fn(), previewPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn(), getPrintingTemplate: vi.fn(), getPrintingTemplateVersion: vi.fn(), listPrintingTemplateVersions: vi.fn(), updatePrintingTemplate: vi.fn() }));
const layout = { ...hostPrintingVersion(), layoutHtml: '<div>私有版式内容</div>' };
let wrapper: VueWrapper | undefined;
beforeEach(() => { vi.resetAllMocks(); const { layoutHtml: _, ...summary } = printTemplate;
  vi.mocked(listPrintingTemplates).mockResolvedValue({ items: [summary], page: 1, pageSize: 20, total: 1 });
  vi.mocked(getPrintingTemplate).mockResolvedValue(printTemplate); vi.mocked(getPrintingTemplateVersion).mockResolvedValue(layout);
  const { layoutHtml: __, ...published } = layout; vi.mocked(listPrintingTemplateVersions).mockResolvedValue({ items: [published], page: 1, pageSize: 20, total: 1 }); });
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
const mountView = (permissions: string[]) => { const context = createOutputSession(permissions); context.session.currentUser = {...context.session.currentUser!,scope:'host',actorScope:'host',tenantId:null}; wrapper = mount(PrintingPreviewView, { global: { plugins: [context.pinia] } }); return context.session; };
async function fillCreate() {
  await wrapper!.get('[data-testid="printing-template-key"]').setValue(printTemplate.templateKey);
  await wrapper!.get('[data-testid="printing-template-name"]').setValue(printTemplate.name);
  await wrapper!.get('[data-testid="printing-template-layout"]').setValue(printTemplate.layoutHtml);
}
describe('打印输出授权与生命周期', () => {
  it('预览失败可重试，已展示内容在注销时立即清理', async () => {
    vi.mocked(getPrintingTemplateVersion).mockRejectedValueOnce(new Error('failed')).mockResolvedValueOnce(layout);
    const session = mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    expect(wrapper!.get('iframe').attributes('srcdoc')).toContain('私有版式内容'); expect(getPrintingTemplateVersion).toHaveBeenCalledTimes(2);
    expect(previewPrintingTemplate).not.toHaveBeenCalled(); session.state = 'anonymous'; await flushPromises(); expect(wrapper!.find('iframe').exists()).toBe(false);
  });
  it('创建后撤销权限不能继续自动发布或预览', async () => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(createPrintingTemplate).mockReturnValue(pending.promise);
    const session = mountView(['printing.templates.read', 'printing.templates.create', 'printing.templates.publish', 'printing.templates.preview']);
    await flushPromises(); await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises();
    await fillCreate();
    await wrapper!.get('[data-testid="printing-preview-submit"]').trigger('click');
    expect(createPrintingTemplate).toHaveBeenCalledOnce(); const signal = vi.mocked(createPrintingTemplate).mock.lastCall![1]!;
    session.currentUser!.permissions = ['printing.templates.read']; pending.resolve(hostPrintingDraft); await flushPromises();
    expect(signal.aborted).toBe(true); expect(publishPrintingTemplate).not.toHaveBeenCalled(); expect(previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('无预览权限不创建操作入口，无读取权限不请求目录', async () => {
    mountView([]); await flushPromises(); expect(wrapper!.find('[data-testid="printing-preview-run"]').exists()).toBe(false); expect(listPrintingTemplates).not.toHaveBeenCalled();
  });
  it('预览撤权后取消请求且迟到结果不恢复敏感内容', async () => {
    const pending = deferred<typeof layout>(); vi.mocked(getPrintingTemplateVersion).mockReturnValue(pending.promise);
    const session = mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); const signal = vi.mocked(getPrintingTemplateVersion).mock.calls[0]?.[2];
    session.currentUser!.permissions = ['printing.templates.read']; await flushPromises(); pending.resolve(layout); await flushPromises();
    expect(signal?.aborted).toBe(true); expect(wrapper!.find('iframe').exists()).toBe(false); expect(wrapper!.find('[data-testid="printing-preview-print"]').exists()).toBe(false);
  });
  it('选择另一模板取消旧预览，不把旧结果当作新模板', async () => {
    const pending = deferred<typeof layout>(); vi.mocked(getPrintingTemplateVersion).mockReturnValue(pending.promise);
    mountView(['printing.templates.read', 'printing.templates.preview']); await flushPromises(); await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click');
    wrapper!.findComponent(ElSelect).vm.$emit('update:modelValue', 'new-template'); wrapper!.findComponent(ElSelect).vm.$emit('change', 'new-template');
    await flushPromises(); pending.resolve(layout); await flushPromises(); expect(wrapper!.find('iframe').exists()).toBe(false);
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
    await fillCreate();
    const button = wrapper!.get('[data-testid="printing-preview-submit"]'); await button.trigger('click'); await button.trigger('click'); pending.resolve({ ...hostPrintingDraft, id: '019bc2b1-2a40-7cc3-8992-a80de51bf292' }); await flushPromises();
    expect(createPrintingTemplate).toHaveBeenCalledTimes(1); expect(publishPrintingTemplate).not.toHaveBeenCalled(); expect(previewPrintingTemplate).not.toHaveBeenCalled();
    expect(wrapper!.get('[data-testid="printing-write-receipt"]').text()).toContain('019bc2b1-2a40-7cc3-8992-a80de51bf292');
  });
  it('打印前重新获取预览，服务端撤权时立即清除旧内容且不调用浏览器打印', async () => {
    vi.mocked(getPrintingTemplateVersion).mockResolvedValueOnce(layout).mockRejectedValueOnce({status:403,code:'authorization.permission_denied',title:'Denied'});
    const print = vi.fn(); vi.spyOn(HTMLIFrameElement.prototype, 'contentWindow', 'get').mockReturnValue({ print } as unknown as Window);
    mountView(['printing.templates.read','printing.templates.preview']); await flushPromises();
    await wrapper!.get('[data-testid="printing-preview-run"]').trigger('click'); await flushPromises();
    expect(wrapper!.get('iframe').attributes('srcdoc')).toContain('私有版式内容');
    await wrapper!.get('[data-testid="printing-preview-print"]').trigger('click'); await flushPromises();
    expect(getPrintingTemplateVersion).toHaveBeenCalledTimes(2);
    expect(print).not.toHaveBeenCalled(); expect(wrapper!.find('iframe').exists()).toBe(false);
  });

});
