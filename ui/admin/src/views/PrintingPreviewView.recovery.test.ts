import { hostPrintingVersion } from '../test/printing-host-fixtures';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import PrintingPreviewView from './PrintingPreviewView.vue';
import * as api from '../api/printing-templates';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, printResult, printTemplate } from '../test/data-output-fixtures';

vi.mock('../api/printing-templates', () => ({ createPrintingTemplate: vi.fn(), listPrintingTemplates: vi.fn(),
  previewPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn() }));
const permissions = ['printing.templates.read', 'printing.templates.create', 'printing.templates.publish', 'printing.templates.preview'];
const other = { ...printTemplate, id: '01980000-0000-7000-8000-000000000003', templateKey: 'other', name: '其他模板' };
let wrapper: VueWrapper | undefined; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listPrintingTemplates).mockResolvedValue([printTemplate, other]);
  vi.mocked(api.createPrintingTemplate).mockResolvedValue({ ...printTemplate, latestPublishedVersionNumber: 0 }); vi.mocked(api.publishPrintingTemplate).mockResolvedValue(hostPrintingVersion());
  vi.mocked(api.previewPrintingTemplate).mockResolvedValue(printResult);
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(granted = permissions, keepAlive = false, tenantId: string | null = null) {
  const context = createShareSession(granted); context.session.currentUser!.tenantId = tenantId; visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(PrintingPreviewView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : PrintingPreviewView, { global: { plugins: [context.pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
function vm() { return wrapper!.findComponent(PrintingPreviewView).vm as unknown as {
  createDialogVisible: boolean; creating: boolean; previewing: boolean; loading: boolean; selectedTemplateId: string;
  createForm: { templateKey: string; name: string; layoutHtml: string };
  load: () => Promise<unknown>; submitCreate: () => Promise<void>; runPreview: (printAfter?: boolean) => Promise<void>;
}; }
async function openCreate() { await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises(); }

function state() { return wrapper!.vm as unknown as { templates: typeof printTemplate[]; creationReceipt?: { templateId: string; outcome: string }; publishSelected: () => Promise<void> }; }
describe('Host 打印创建发布恢复', () => {
  it('创建已成功但发布失败时关闭旧表单并保留草稿回执', async () => {
    await setup(); await openCreate(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await vm().submitCreate();
    expect(vm().createDialogVisible).toBe(false); expect(state().creationReceipt).toMatchObject({ templateId: printTemplate.id, outcome: 'publishUnconfirmed' });
    await vm().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('未确认发布不能重发，刷新确认仍为草稿后显式发布而不重新创建', async () => {
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, latestPublishedVersionNumber: 0 }, other]); await setup(); await openCreate(); vi.mocked(api.createPrintingTemplate).mockResolvedValueOnce({ ...printTemplate, latestPublishedVersionNumber: 0 }); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await vm().submitCreate();
    await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, latestPublishedVersionNumber: 0, version: 2 }]); await vm().load(); await state().publishSelected();
    expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); expect(api.publishPrintingTemplate).toHaveBeenLastCalledWith(printTemplate.id, { version: 2 }, expect.any(AbortSignal));
  });
  it('刷新确认已发布后不再次发布草稿', async () => {
    await setup(); await openCreate(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await vm().submitCreate(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, version: 2 }]); await vm().load(); await state().publishSelected();
    expect(state().creationReceipt?.outcome).toBe('published'); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('发布未知后目录刷新失败不解除再次发布门槛', async () => {
    await setup(); state().templates = [{ ...printTemplate, latestPublishedVersionNumber: 0 }]; vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await state().publishSelected();
    vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline')); await vm().load(); await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce(); expect(state().creationReceipt?.outcome).toBe('publishUnconfirmed');
  });
  it('切换并发布其他草稿不能覆盖原模板的未知发布门槛', async () => {
    await setup(); state().templates = [{ ...printTemplate, latestPublishedVersionNumber: 0 }, { ...other, latestPublishedVersionNumber: 0 }]; vi.mocked(api.publishPrintingTemplate).mockRejectedValue(new Error('lost'));
    await state().publishSelected(); vm().selectedTemplateId = other.id; await state().publishSelected(); vm().selectedTemplateId = printTemplate.id; await state().publishSelected();
    expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('其他回执覆盖后已确认发布仍不能被失败补读降级为可重发', async () => {
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, latestPublishedVersionNumber: 0 }, { ...other, latestPublishedVersionNumber: 0 }]);
    await setup(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline'));
    await state().publishSelected(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, version: 2 }, { ...other, latestPublishedVersionNumber: 0 }]);
    await vm().load(); vm().selectedTemplateId = other.id; vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await state().publishSelected();
    vm().selectedTemplateId = printTemplate.id; await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('草稿发布在途切换主体后丢弃迟到结果和回执', async () => {
    const session = await setup(); state().templates = [{ ...printTemplate, latestPublishedVersionNumber: 0 }]; const pending = deferred<ReturnType<typeof hostPrintingVersion>>(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise);
    const action = state().publishSelected(); session.currentUser!.sessionId = 'next'; await flushPromises(); pending.resolve(hostPrintingVersion()); await action; expect(state().creationReceipt).toBeUndefined();
  });
  it('缺少发布权限不能直接发布已有草稿', async () => {
    await setup(permissions.filter(code => code !== 'printing.templates.publish')); state().templates = [{ ...printTemplate, latestPublishedVersionNumber: 0 }]; await state().publishSelected(); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('关闭在途发布保留已创建但发布未确认的回执', async () => {
    await setup(); await openCreate(); const pending = deferred<ReturnType<typeof hostPrintingVersion>>(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise); const action = vm().submitCreate(); await flushPromises(); vm().createDialogVisible = false; pending.resolve(hostPrintingVersion()); await action;
    expect(state().creationReceipt?.outcome).toBe('publishUnconfirmed');
  });
  it('旧创建窗口关闭和提交回调不能影响新窗口', async () => {
    await setup(); await openCreate(); const buttons = wrapper!.findAllComponents({ name: 'ElButton' });
    const submit = buttons.find(button => button.attributes('data-testid') === 'printing-preview-submit')!.vm.$.vnode.props!.onClick as () => void;
    const close = buttons.find(button => button.attributes('data-testid') === 'printing-preview-create-back')!.vm.$.vnode.props!.onClick as () => void;
    vm().createDialogVisible = false; await openCreate(); close(); submit(); await flushPromises(); expect(vm().createDialogVisible).toBe(true); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
  });
  it('Host模板内容变化取消等待预览，不接入旧内容', async () => {
    await setup(); const pending = deferred<typeof printResult>(); vi.mocked(api.previewPrintingTemplate).mockReturnValueOnce(pending.promise); const action = vm().runPreview();
    state().templates = [{ ...printTemplate, version: 2 }]; pending.resolve(printResult); await action; expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
  it('Host预览接受等价UUID，拒绝错误表单归属', async () => {
    await setup(); vm().selectedTemplateId = printTemplate.id.toUpperCase(); vi.mocked(api.previewPrintingTemplate).mockResolvedValueOnce({ ...printResult, templateId: printTemplate.id.toUpperCase() }); await vm().runPreview(); expect(wrapper!.text()).toContain('旧租户敏感内容');
    vi.mocked(api.previewPrintingTemplate).mockResolvedValueOnce({ ...printResult, formSchemaKey: 'other' }); await vm().runPreview(); expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
});
