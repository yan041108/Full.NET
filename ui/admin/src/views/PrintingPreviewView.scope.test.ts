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
  acknowledgeRecovery: (kind: 'create') => Promise<void>; finishRecovery: (value: boolean) => void;
  load: () => Promise<unknown>; submitCreate: () => Promise<void>; runPreview: (printAfter?: boolean) => Promise<void>;
}; }
async function openCreate() { await flushPromises(); await wrapper!.get('[data-testid="printing-preview-create"]').trigger('click'); await flushPromises(); }

describe('Host 打印创建、目录与输出归属', () => {
  it('缺少页面读取权限不显示创建入口或发送直接操作', async () => {
    await setup(permissions.filter(code => code !== 'printing.templates.read')); vm().createDialogVisible = true;
    await vm().submitCreate(); vm().selectedTemplateId = printTemplate.id; await vm().runPreview();
    expect(api.listPrintingTemplates).not.toHaveBeenCalled(); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
    expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="printing-preview-create"]').exists()).toBe(false);
  });
  it('Host 文本作用域带租户不能进入页面', async () => {
    await setup(permissions, false, printTemplate.id); expect(api.listPrintingTemplates).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="printing-preview-run"]').exists()).toBe(false);
  });
  it('未打开创建弹窗不能直接提交', async () => { await setup(); await vm().submitCreate(); expect(api.createPrintingTemplate).not.toHaveBeenCalled(); });
  it.each(['resolve', 'reject'] as const)('关闭创建取消请求，迟到%s不能发布、刷新或反馈', async outcome => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise);
    const success = vi.spyOn(feedback, 'showSuccess'); await setup(); await openCreate(); const action = vm().submitCreate();
    const signal = vi.mocked(api.createPrintingTemplate).mock.calls[0]![1]!; vm().createDialogVisible = false;
    expect(signal.aborted).toBe(true); if (outcome === 'resolve') pending.resolve(printTemplate); else pending.reject(new Error('late'));
    await action; expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); expect(api.listPrintingTemplates).toHaveBeenCalledOnce();
    expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); expect(success).not.toHaveBeenCalled();
  });
  it('关闭发布等待取消同一创建流程，不继续读取目录', async () => {
    const pending = deferred<ReturnType<typeof hostPrintingVersion>>(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise);
    await setup(); await openCreate(); const action = vm().submitCreate(); await flushPromises();
    const signal = vi.mocked(api.publishPrintingTemplate).mock.calls[0]![2]!; vm().createDialogVisible = false;
    expect(signal.aborted).toBe(true); pending.resolve(hostPrintingVersion()); await action;
    expect(api.listPrintingTemplates).toHaveBeenCalledOnce(); expect(api.previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('关闭创建后的目录刷新取消子请求，不在新弹窗选中旧模板', async () => {
    const pending = deferred<typeof printTemplate[]>(); await setup(); vi.mocked(api.listPrintingTemplates).mockReturnValueOnce(pending.promise);
    await openCreate(); const action = vm().submitCreate(); await flushPromises();
    const signal = vi.mocked(api.listPrintingTemplates).mock.calls[1]![1]!; vm().createDialogVisible = false; await openCreate();
    expect(signal.aborted).toBe(true); pending.resolve([other]); await action; expect(vm().createDialogVisible).toBe(true);
    expect(api.previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('关闭创建流程中的自动预览取消子请求，不插入迟到内容', async () => {
    const pending = deferred<typeof printResult>(); vi.mocked(api.previewPrintingTemplate).mockReturnValueOnce(pending.promise);
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, version: 2 }]); await openCreate(); const action = vm().submitCreate(); await flushPromises();
    const signal = vi.mocked(api.previewPrintingTemplate).mock.calls[0]![2]!; vm().createDialogVisible = false;
    expect(signal.aborted).toBe(true); pending.resolve(printResult); await action;
    expect(wrapper!.text()).not.toContain('旧租户敏感内容'); expect(vm().previewing).toBe(false);
  });
  it('旧创建 finally 不清除重开后新请求的忙状态', async () => {
    const old = deferred<typeof printTemplate>(); const next = deferred<typeof printTemplate>();
    vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise);
    await setup(); await openCreate(); const first = vm().submitCreate(); vm().createDialogVisible = false; await openCreate();
    const recovery = vm().acknowledgeRecovery('create'); vm().finishRecovery(true); await recovery;
    const second = vm().submitCreate(); old.resolve(printTemplate); await first; expect(vm().creating).toBe(true);
    expect(vm().createDialogVisible).toBe(true); vm().createDialogVisible = false; next.resolve(printTemplate); await second;
  });
  it('关闭弹窗清空旧表单，新建从默认值开始', async () => {
    await setup(); await openCreate(); vm().createForm.name = '私有输入'; vm().createForm.layoutHtml = '私有布局';
    vm().createDialogVisible = false; await openCreate(); expect(vm().createForm.name).not.toBe('私有输入'); expect(vm().createForm.layoutHtml).not.toBe('私有布局');
  });
  it('健康创建按原版本发布，再选中目录中的新模板并预览', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, version: 2 }]); await openCreate(); await vm().submitCreate(); await flushPromises();
    expect(api.publishPrintingTemplate).toHaveBeenCalledWith(printTemplate.id, { version: printTemplate.version }, expect.any(AbortSignal));
    expect(api.listPrintingTemplates).toHaveBeenCalledTimes(2); expect(api.previewPrintingTemplate).toHaveBeenCalledWith(printTemplate.id, { versionNumber: 1 }, expect.any(AbortSignal));
    expect(vm().createDialogVisible).toBe(false); expect(wrapper!.text()).toContain('旧租户敏感内容');
  });
  it('创建结果不在当前目录时不能自动选中或预览', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([other]); await openCreate(); await vm().submitCreate();
    expect(vm().selectedTemplateId).toBe(other.id); expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); expect(vm().createDialogVisible).toBe(false);
  });
  it('成功创建后的刷新失败仍关闭已提交表单，不继续预览', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('refresh failed'));
    await openCreate(); await vm().submitCreate(); expect(vm().createDialogVisible).toBe(false); expect(api.previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('创建被明确拒绝后保留当前表单，允许修正再手动重试', async () => {
    vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: '输入无效' }); await setup(); await openCreate();
    await vm().submitCreate(); expect(vm().createDialogVisible).toBe(true); expect(vm().creating).toBe(false);
    await vm().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('创建期间撤销页面 Read，取消原请求且不接入旧结果', async () => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise);
    const session = await setup(); await openCreate(); const action = vm().submitCreate(); const signal = vi.mocked(api.createPrintingTemplate).mock.calls[0]![1]!;
    session.currentUser!.permissions = permissions.filter(code => code !== 'printing.templates.read'); pending.resolve(printTemplate); await action;
    expect(signal.aborted).toBe(true); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('目录刷新保留仍存在的选择，消失后才选取默认模板', async () => {
    await setup(); vm().selectedTemplateId = other.id; await vm().load(); expect(vm().selectedTemplateId).toBe(other.id);
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([printTemplate]); await vm().load(); expect(vm().selectedTemplateId).toBe(printTemplate.id);
  });
  it.each(['resolve', 'reject'] as const)('较新目录已完成时忽略旧目录%s', async outcome => {
    const pending = deferred<typeof printTemplate[]>(); await setup(); vi.mocked(api.listPrintingTemplates).mockReturnValueOnce(pending.promise);
    const first = vm().load(); const signal = vi.mocked(api.listPrintingTemplates).mock.calls[1]![1]!; await vm().load();
    if (outcome === 'resolve') pending.resolve([other]); else pending.reject(new Error('stale directory'));
    await first; expect(signal.aborted).toBe(true); expect(vm().selectedTemplateId).toBe(printTemplate.id); expect(vm().loading).toBe(false);
  });
  it('任意非目录 ID 不能发起预览', async () => {
    await setup(); vm().selectedTemplateId = '01980000-0000-7000-8000-000000000099'; await vm().runPreview();
    expect(api.previewPrintingTemplate).not.toHaveBeenCalled();
  });
  it('直接改变选择也取消预览，不依赖 Select 的 change 事件', async () => {
    const pending = deferred<typeof printResult>(); vi.mocked(api.previewPrintingTemplate).mockReturnValueOnce(pending.promise);
    await setup(); const action = vm().runPreview(); const signal = vi.mocked(api.previewPrintingTemplate).mock.calls[0]![2]!;
    vm().selectedTemplateId = other.id; expect(signal.aborted).toBe(true); pending.resolve(printResult); await action;
    expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
  it('打印重新请求展示过的精确版本，版本错配不调用浏览器打印', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {}); await setup(); await vm().runPreview();
    vi.mocked(api.previewPrintingTemplate).mockResolvedValueOnce({ ...printResult, versionNumber: 2 }); await vm().runPreview(true);
    expect(api.previewPrintingTemplate).toHaveBeenLastCalledWith(printTemplate.id, { versionNumber: 1 }, expect.any(AbortSignal));
    expect(print).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain('旧租户敏感内容');
  });
  it('健康打印在重新授权同一版本后执行一次', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {}); await setup(); await vm().runPreview(); await vm().runPreview(true);
    expect(print).toHaveBeenCalledOnce();
  });
  it('没有展示内容不能以打印入口直接请求最新版本', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {}); await setup(); await vm().runPreview(true);
    expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); expect(print).not.toHaveBeenCalled();
  });
  it('KeepAlive 停用取消创建和预览，恢复只读取当前目录', async () => {
    const pending = deferred<typeof printTemplate>(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise);
    await setup(permissions, true); await openCreate(); const action = vm().submitCreate(); const signal = vi.mocked(api.createPrintingTemplate).mock.calls[0]![1]!;
    visible.value = false; await flushPromises(); expect(signal.aborted).toBe(true); pending.resolve(printTemplate); await action;
    visible.value = true; await flushPromises(); expect(api.listPrintingTemplates).toHaveBeenCalledTimes(2);
    expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); expect(api.previewPrintingTemplate).not.toHaveBeenCalled();
  });
});
