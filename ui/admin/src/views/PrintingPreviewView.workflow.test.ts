import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElInput, ElSelect } from 'element-plus';
import View from './PrintingPreviewView.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/printing-templates';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, printResult, printTemplate } from '../test/data-output-fixtures';
import { hostPrintingDraft as draft, hostPrintingVersion } from '../test/printing-host-fixtures';
vi.mock('../api/printing-templates', () => ({ listPrintingTemplates: vi.fn(), createPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn(), previewPrintingTemplate: vi.fn() }));
const permissions = ['read', 'create', 'publish', 'preview', 'grant_tenants'].map(action => 'printing.templates.' + action);
const failure = { title: '服务暂时不可用', code: 'fixture.unavailable', status: 503 };
let wrapper: VueWrapper;
interface State {
  templates: typeof printTemplate[]; selectedTemplateId: string; preview?: typeof printResult; problem?: unknown; catalogProblem?: unknown; catalogReady: boolean;
  createDialogVisible: boolean; creating: boolean; previewing: boolean; publishing: boolean; createBlocked: boolean; createOutcome?: string;
  createForm: { templateKey: string; name: string; layoutHtml: string }; creationReceipt?: { templateId: string; outcome: string; versionNumber?: number };
  load: () => Promise<boolean>; openCreate: () => void; submitCreate: () => Promise<void>; publishSelected: () => Promise<void>; runPreview: (print?: boolean) => Promise<void>;
  acknowledgeRecovery: (kind: 'create' | 'publish') => Promise<void>; finishRecovery: (value: boolean) => void;
}
const state = () => wrapper.vm as unknown as State;
const GrantStub = defineComponent({ name: 'PrintingTenantGrantsDialog', props: ['template'], emits: ['close'], template: '<div />' });
async function setup(route?: { name: string }) {
  const context = createShareSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], stubs: { PrintingTenantGrantsDialog: GrantStub }, provide: route ? { [routeLocationKey as symbol]: route } : {} } }); await flushPromises(); return context.session;
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listPrintingTemplates).mockResolvedValue([{ ...printTemplate }]);
  vi.mocked(api.createPrintingTemplate).mockImplementation(async body => ({ ...draft, ...body }));
  vi.mocked(api.publishPrintingTemplate).mockResolvedValue(hostPrintingVersion()); vi.mocked(api.previewPrintingTemplate).mockResolvedValue(printResult);
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
describe('Host 打印完整流程恢复', () => {
  it('目录读取失败保留确认选择，但阻止预览与发布，重试成功恢复门槛', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(failure); await state().load();
    expect(state().templates).toHaveLength(1); expect(state().selectedTemplateId).toBe(printTemplate.id); expect(state().catalogReady).toBe(false);
    await state().runPreview(); expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); await state().load(); expect(state().catalogProblem).toBeUndefined(); expect(state().catalogReady).toBe(true);
  });
  it('重复身份目录不替换已有目录或放开操作', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([printTemplate, { ...printTemplate, id: printTemplate.id.toUpperCase() }]);
    await state().load(); expect(state().catalogProblem).toBeDefined(); expect(state().catalogReady).toBe(false); expect(state().templates).toHaveLength(1);
  });
  it('观察到新修订后后续目录不允许回退', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...printTemplate, version: 4, latestPublishedVersionNumber: 2 }]); await state().load();
    await state().load(); expect(state().templates[0]!.version).toBe(4); expect(state().catalogProblem).toBeDefined();
  });
  it('创建响应丢失后关闭重开仍禁止再次提交，人工确认只解锁', async () => {
    await setup(); state().openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(failure); await state().submitCreate();
    expect(state().createOutcome).toBe('unconfirmed'); state().createDialogVisible = false; state().openCreate(); await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
    const recovery = state().acknowledgeRecovery('create'); state().finishRecovery(true); await recovery;
    expect(state().createBlocked).toBe(false); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('明确拒绝的创建保留表单，允许修正后手动再提交', async () => {
    await setup(); state().openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: '输入无效' });
    await state().submitCreate(); expect(state().createBlocked).toBe(false); expect(state().createOutcome).toBe('failed'); expect(state().createDialogVisible).toBe(true);
  });
  it.each(['close', 'input'] as const)('创建发送后 %s 标记未知结果，迟到结果不自动发布', async kind => {
    const pending = deferred<typeof printTemplate>(); await setup(); state().openCreate(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise); const action = state().submitCreate();
    if (kind === 'close') state().createDialogVisible = false; else state().createForm.name = '后改输入';
    pending.resolve({ ...draft }); await action; expect(state().createBlocked).toBe(true); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('发布回包必须对应模板、布局、发布用户和精确版本', async () => {
    vi.mocked(api.listPrintingTemplates).mockResolvedValue([draft]); await setup(); vi.mocked(api.publishPrintingTemplate).mockResolvedValueOnce({ ...hostPrintingVersion(), layoutHtml: '<div>其他内容</div>' });
    await state().publishSelected(); expect(state().creationReceipt?.outcome).toBe('publishUnconfirmed'); expect(api.listPrintingTemplates).toHaveBeenCalledOnce();
  });
  it('发布成功先保存精确版本回执，旧目录和读取失败不会降级或重发', async () => {
    vi.mocked(api.listPrintingTemplates).mockResolvedValue([draft]); await setup(); await state().publishSelected();
    expect(state().creationReceipt).toMatchObject({ templateId: draft.id, outcome: 'published', versionNumber: 1 });
    expect(state().catalogProblem).toBeDefined(); await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('未知发布后的相同草稿读取不能解除保护，新修订读取可恢复', async () => {
    vi.mocked(api.listPrintingTemplates).mockResolvedValue([draft]); await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(failure); await state().publishSelected();
    await state().load(); await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([{ ...draft, version: 2 }]); await state().load();
    await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('已知创建成功但发布失败后只恢复发布，不再次创建', async () => {
    await setup(); state().openCreate(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(failure); await state().submitCreate();
    expect(state().createDialogVisible).toBe(false); expect(state().creationReceipt?.outcome).toBe('publishUnconfirmed'); expect(state().createBlocked).toBe(false);
    const recovery = state().acknowledgeRecovery('publish'); state().finishRecovery(true); await recovery; expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('健康创建、发布、目录补读和预览使用同一新资源', async () => {
    await setup(); state().openCreate(); const created = { ...draft, ...state().createForm, id: '019bc2b1-2a40-7cc3-8992-a80de51bf292' };
    vi.mocked(api.createPrintingTemplate).mockResolvedValueOnce(created); vi.mocked(api.publishPrintingTemplate).mockResolvedValueOnce(hostPrintingVersion(created));
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([printTemplate, { ...created, version: 2, latestPublishedVersionNumber: 1 }]);
    vi.mocked(api.previewPrintingTemplate).mockResolvedValueOnce({ ...printResult, templateId: created.id, templateKey: created.templateKey, templateName: created.name });
    await state().submitCreate(); expect(state().creationReceipt).toMatchObject({ templateId: created.id, outcome: 'published', versionNumber: 1 });
    expect(state().selectedTemplateId).toBe(created.id); expect(state().preview?.templateId).toBe(created.id); expect(state().createDialogVisible).toBe(false);
  });
  it('净化为空的草稿仍保存创建回执，明确发布拒绝不变成未知创建', async () => {
    await setup(); state().openCreate(); const created = { ...draft, ...state().createForm, id: '019bc2b1-2a40-7cc3-8992-a80de51bf292', layoutHtml: '' };
    vi.mocked(api.createPrintingTemplate).mockResolvedValueOnce(created); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid_layout', title: '布局无效' });
    await state().submitCreate(); expect(state().creationReceipt).toMatchObject({ templateId: created.id, outcome: 'publishFailed' });
    expect(state().createBlocked).toBe(false); expect(state().createOutcome).toBeUndefined(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
    vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce([printTemplate, created]); await state().load(); expect(state().catalogReady).toBe(true); expect(state().catalogProblem).toBeUndefined();
  });
  it('普通预览也冻结目录确认的精确版本，错模板机器键不可显示', async () => {
    await setup(); vi.mocked(api.previewPrintingTemplate).mockResolvedValueOnce({ ...printResult, templateKey: 'other' }); await state().runPreview();
    expect(api.previewPrintingTemplate).toHaveBeenLastCalledWith(printTemplate.id, { versionNumber: 1 }, expect.any(AbortSignal)); expect(state().preview).toBeUndefined();
  });
  it('打印仍重新授权精确版本，打印调用失败保留新预览供人工重试', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => { throw Error('fixture.print_unavailable'); }); await setup(); await state().runPreview(); await state().runPreview(true);
    expect(print).toHaveBeenCalledOnce(); expect(state().preview).toBeDefined(); expect(state().problem).toBeDefined(); expect(api.previewPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it('旧目录按钮、选择和预览回调不能操作刷新后的资源', async () => {
    await setup(); const refresh = wrapper.getComponent(ArtTableHeader).vm.$.vnode.props!.onRefresh as () => void;
    const select = wrapper.getComponent(ElSelect).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    const run = wrapper.findAllComponents(ElButton).find(button => button.attributes('data-testid') === 'printing-preview-run')!.vm.$.vnode.props!.onClick as () => void;
    await state().load(); refresh(); select('stale'); run(); await flushPromises(); expect(api.listPrintingTemplates).toHaveBeenCalledTimes(2); expect(api.previewPrintingTemplate).not.toHaveBeenCalled(); expect(state().selectedTemplateId).toBe(printTemplate.id);
  });
  it('旧创建输入、提交和关闭回调不能接入重开的表单', async () => {
    await setup(); state().openCreate(); await flushPromises(); const dialog = wrapper.getComponent(ElDialog), input = dialog.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    const close = dialog.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
    const submit = dialog.findAllComponents(ElButton).find(button => button.attributes('data-testid') === 'printing-preview-submit')!.vm.$.vnode.props!.onClick as () => void;
    state().createDialogVisible = false; state().openCreate(); input('stale'); close(false); submit(); await flushPromises();
    expect(state().createForm.templateKey).not.toBe('stale'); expect(state().createDialogVisible).toBe(true); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
  });
  it('旧恢复确认不能解锁下一次确认，确认本身不发请求', async () => {
    await setup(); state().openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(failure); await state().submitCreate();
    const first = state().acknowledgeRecovery('create'); await flushPromises(); const confirm = wrapper.getComponent(ArtFormDialog).vm.$.vnode.props!.onConfirm as () => void;
    state().finishRecovery(false); await first; const second = state().acknowledgeRecovery('create'); confirm(); await flushPromises(); expect(state().createBlocked).toBe(true);
    state().finishRecovery(false); await second; expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('同一模板重开授权弹窗后，旧关闭回调不能关闭新窗口', async () => {
    await setup(); const open = () => wrapper.findAllComponents(ElButton).find(button => button.attributes('data-testid') === 'printing-tenant-grants-open')!.vm.$emit('click');
    open(); await flushPromises(); const first = wrapper.getComponent(GrantStub), oldClose = first.vm.$.vnode.props!.onClose as () => void;
    first.vm.$emit('close'); await flushPromises(); open(); await flushPromises(); oldClose(); await flushPromises(); expect(wrapper.findComponent(GrantStub).exists()).toBe(true);
  });
  it('离开路由取消预览并清空创建布局，旧结果不能重新插入 DOM', async () => {
    const route = reactive({ name: 'printing-preview' }), pending = deferred<typeof printResult>(); await setup(route);
    vi.mocked(api.previewPrintingTemplate).mockReturnValueOnce(pending.promise); const action = state().runPreview(); route.name = 'home'; pending.resolve(printResult); await action;
    expect(state().preview).toBeUndefined(); expect(wrapper.text()).not.toContain('旧租户敏感内容'); expect(vi.mocked(api.previewPrintingTemplate).mock.calls[0]![2]!.aborted).toBe(true);
  });
});
