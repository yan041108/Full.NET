import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElInput, ElSelect } from 'element-plus';
import type { PrintingTemplate, PrintingTemplateSummary } from '@fullnet/client-contracts';
import View from './PrintingPreviewView.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/printing-templates';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred, printTemplate } from '../test/data-output-fixtures';
import { hostPrintingDraft as draft, hostPrintingVersion } from '../test/printing-host-fixtures';
vi.mock('../api/printing-templates', () => ({ listPrintingTemplates: vi.fn(), createPrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn(), previewPrintingTemplate: vi.fn(), getPrintingTemplate: vi.fn(), getPrintingTemplateVersion: vi.fn(), listPrintingTemplateVersions: vi.fn(), updatePrintingTemplate: vi.fn(), getPrintingFormSchema: vi.fn() }));
const permissions = ['read', 'create', 'publish', 'preview', 'grant_tenants'].map(action => 'printing.templates.' + action);
const failure = { title: '服务暂时不可用', code: 'fixture.unavailable', status: 503 };
const created = { ...draft, id: '019bc2b1-2a40-7cc3-8992-a80de51bf292', templateKey: 'created-fixture', name: '新建模板' };
const summary = ({ layoutHtml: _, ...row }: PrintingTemplate): PrintingTemplateSummary => row;
function versionBody(row: PrintingTemplate, number = row.latestPublishedVersionNumber) { return { ...hostPrintingVersion({ ...row, latestPublishedVersionNumber: number - 1 }), id: '019bc2b1-2a40-7cc3-8992-' + (1000 + number).toString(16).padStart(12, '0') }; }
let wrapper: VueWrapper, catalog: PrintingTemplate[];
const find = (id: string) => catalog.find(row => row.id.toLowerCase() === id.toLowerCase())!;
interface State {
  templates: PrintingTemplateSummary[]; selectedTemplateId: string; preview?: { html: string; versionNumber: number }; problem?: unknown; catalogProblem?: unknown; catalogReady: boolean;
  createDialogVisible: boolean; creating: boolean; createBlocked: boolean; createForm: { templateKey: string; name: string; layoutHtml: string };
  writeReceipt?: { templateId?: string; kind: string; outcome: string; versionNumber?: number }; recoveryPrompt?: { confirm: () => void; close: (open: boolean) => void };
  load: () => Promise<void>; openCreate: () => void; submitCreate: () => Promise<void>; confirmPublish: () => Promise<void>; publishSelected: () => Promise<void>; runPreview: () => Promise<void>; printPreview: () => Promise<void>; acknowledgeRecovery: () => void;
}
const state = () => wrapper.vm as unknown as State;
const GrantStub = defineComponent({ name: 'PrintingTenantGrantsDialog', props: ['template'], emits: ['close'], template: '<div />' });
async function setup(route?: { name: string }) { const context = createShareSession(permissions); wrapper = mount(View, { global: { plugins: [context.pinia], stubs: { PrintingTenantGrantsDialog: GrantStub }, provide: route ? { [routeLocationKey as symbol]: route } : {} } }); await flushPromises(); return context.session; }
async function prepareCreate() { state().openCreate(); Object.assign(state().createForm, { templateKey: created.templateKey, name: created.name, layoutHtml: created.layoutHtml }); await flushPromises(); }
async function publish() { await state().confirmPublish(); await state().publishSelected(); }
beforeEach(() => {
  vi.resetAllMocks(); catalog = [{ ...printTemplate }];
  vi.mocked(api.listPrintingTemplates).mockImplementation(async () => ({ items: catalog.map(summary), total: catalog.length, page: 1, pageSize: 20 }));
  vi.mocked(api.getPrintingTemplate).mockImplementation(async id => ({ ...find(id) }));
  vi.mocked(api.getPrintingTemplateVersion).mockImplementation(async (id, number) => versionBody(find(id), number));
  vi.mocked(api.listPrintingTemplateVersions).mockImplementation(async id => { const target = find(id), items = Array.from({ length: target.latestPublishedVersionNumber }, (_item, index) => { const { layoutHtml: _layout, ...row } = versionBody(target, target.latestPublishedVersionNumber - index); return row; }); return { items, total: items.length, page: 1, pageSize: 20 }; });
  vi.mocked(api.createPrintingTemplate).mockImplementation(async input => { const row = { ...created, ...input }; catalog.push(row); return row; });
  vi.mocked(api.publishPrintingTemplate).mockImplementation(async (id, input) => { const target = find(id), result = { ...versionBody(target, target.latestPublishedVersionNumber + 1), changeNote: input.changeNote ?? null }; catalog = catalog.map(row => row.id === target.id ? { ...row, version: input.version + 1, latestPublishedVersionNumber: result.versionNumber } : row); return result; });
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
describe('Host 打印完整流程恢复', () => {
  it('目录失败保留选择但阻止预览和发布，人工重试成功恢复准入', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(failure); await state().load(); expect(state().templates).toHaveLength(1); expect(state().selectedTemplateId).toBe(printTemplate.id); expect(state().catalogReady).toBe(false);
    await state().runPreview(); await publish(); expect(api.getPrintingTemplateVersion).not.toHaveBeenCalled(); expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); await state().load(); expect(state().catalogProblem).toBeUndefined(); expect(state().catalogReady).toBe(true);
  });
  it('重复身份目录不替换可信目录或放开操作', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce({ items: [summary(printTemplate), { ...summary(printTemplate), id: printTemplate.id.toUpperCase() }], total: 2, page: 1, pageSize: 20 });
    await state().load(); expect(state().catalogProblem).toBeDefined(); expect(state().catalogReady).toBe(false); expect(state().templates).toHaveLength(1);
  });
  it('观察新修订后后续目录不允许回退', async () => {
    await setup(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce({ items: [{ ...summary(printTemplate), version: 4, latestPublishedVersionNumber: 2 }], total: 1, page: 1, pageSize: 20 }); await state().load();
    await state().load(); expect(state().templates[0]!.version).toBe(4); expect(state().catalogProblem).toBeDefined();
  });
  it('新建响应丢失后关闭重开仍禁止重复，人工确认只解锁', async () => {
    await setup(); await prepareCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(failure); await state().submitCreate(); expect(state().writeReceipt?.outcome).toBe('unconfirmed');
    state().createDialogVisible = false; await prepareCreate(); await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); state().acknowledgeRecovery(); state().recoveryPrompt!.confirm(); expect(state().createBlocked).toBe(false); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('明确拒绝保留表单，允许修正后手动重试', async () => {
    await setup(); await prepareCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: '输入无效' }); await state().submitCreate();
    expect(state().createBlocked).toBe(false); expect(state().writeReceipt?.outcome).toBe('failed'); expect(state().createDialogVisible).toBe(true); await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledTimes(2);
  });
  it.each(['close', 'input'] as const)('创建发送后 %s 标记未知，迟到结果不自动发布', async kind => {
    const pending = deferred<typeof printTemplate>(); await setup(); await prepareCreate(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise); const action = state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
    if (kind === 'close') state().createDialogVisible = false; else state().createForm.name = '后改输入'; pending.resolve(created); await action; expect(state().createBlocked).toBe(true); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
  });
  it('发布回包必须对应模板布局、发布人和精确版本', async () => {
    catalog = [{ ...draft }]; await setup(); vi.mocked(api.publishPrintingTemplate).mockResolvedValueOnce({ ...versionBody(draft, 1), layoutHtml: '<div>其他内容</div>' }); await publish();
    expect(state().writeReceipt?.outcome).toBe('unconfirmed'); expect(api.listPrintingTemplates).toHaveBeenCalledOnce();
  });
  it('发布成功先保存精确回执，陈旧目录和读取失败不降级或重发', async () => {
    catalog = [{ ...draft }]; await setup(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(failure); await publish(); expect(state().writeReceipt).toMatchObject({ templateId: draft.id, kind: 'publish', outcome: 'succeeded', versionNumber: 1 });
    expect(state().catalogProblem).toBeDefined(); await state().publishSelected(); vi.mocked(api.listPrintingTemplates).mockResolvedValueOnce({ items: [summary(draft)], total: 1, page: 1, pageSize: 20 }); await state().load();
    expect(state().templates[0]).toMatchObject({ version: 2, latestPublishedVersionNumber: 1 }); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('未知发布同草稿读取不解锁，新修订读取后仍须人工再次确认', async () => {
    catalog = [{ ...draft }]; await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(failure); await publish(); await state().load(); await publish(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
    catalog[0] = { ...draft, version: 2 }; await state().load(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce(); await publish(); expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2); expect(vi.mocked(api.publishPrintingTemplate).mock.lastCall![1].version).toBe(2);
  });
  it('已知新建成功但独立发布失败只恢复发布，不再次新建', async () => {
    await setup(); await prepareCreate(); await state().submitCreate(); expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(failure); await publish();
    expect(state().createDialogVisible).toBe(false); expect(state().writeReceipt).toMatchObject({ kind: 'publish', outcome: 'unconfirmed' }); expect(state().createBlocked).toBe(false); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('健康新建、独立发布、目录补读和版式预览使用同一新资源', async () => {
    await setup(); await prepareCreate(); await state().submitCreate(); expect(state().writeReceipt).toMatchObject({ templateId: created.id, kind: 'create', outcome: 'succeeded' }); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
    await publish(); await state().runPreview(); expect(state().writeReceipt).toMatchObject({ templateId: created.id, kind: 'publish', outcome: 'succeeded', versionNumber: 1 });
    expect(state().selectedTemplateId).toBe(created.id); expect(state().preview?.versionNumber).toBe(1); expect(api.getPrintingTemplateVersion).toHaveBeenCalledWith(created.id, 1, expect.any(AbortSignal)); expect(state().createDialogVisible).toBe(false);
  });
  it('净化为空仍保留已新建回执，明确发布拒绝不变成未知新建', async () => {
    await setup(); await prepareCreate(); vi.mocked(api.createPrintingTemplate).mockImplementationOnce(async input => { const row = { ...created, ...input, layoutHtml: '' }; catalog.push(row); return row; }); await state().submitCreate();
    expect(state().writeReceipt).toMatchObject({ templateId: created.id, kind: 'create', outcome: 'succeeded' }); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid_layout', title: '布局无效' }); await publish();
    expect(state().writeReceipt).toMatchObject({ templateId: created.id, kind: 'publish', outcome: 'failed' }); expect(state().createBlocked).toBe(false); expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); await state().load(); expect(state().catalogReady).toBe(true);
  });
  it('普通预览冻结精确发布号，其他模板的版本正文不可显示', async () => {
    await setup(); vi.mocked(api.getPrintingTemplateVersion).mockResolvedValueOnce({ ...versionBody(printTemplate, 1), templateId: created.id }); await state().runPreview();
    expect(api.getPrintingTemplateVersion).toHaveBeenLastCalledWith(printTemplate.id, 1, expect.any(AbortSignal)); expect(state().preview).toBeUndefined();
  });
  it('打印重读精确版本，打印调用失败保留新预览供人工重试', async () => {
    const print = vi.fn(() => { throw Error('fixture.print_unavailable'); }); vi.spyOn(HTMLIFrameElement.prototype, 'contentWindow', 'get').mockReturnValue({ print } as unknown as Window);
    await setup(); await state().runPreview(); await state().printPreview(); await flushPromises(); await wrapper.get('iframe').trigger('load'); expect(print).toHaveBeenCalledOnce(); expect(state().preview).toBeDefined(); expect(state().problem).toBeDefined(); expect(api.getPrintingTemplateVersion).toHaveBeenCalledTimes(2);
  });
  it('旧目录按钮、选择和预览回调不能操作刷新后的资源', async () => {
    await setup(); const refresh = wrapper.getComponent(ArtTableHeader).vm.$.vnode.props!.onRefresh as () => void, select = wrapper.findAllComponents(ElSelect)[0]!.vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    const run = wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'printing-preview-run')!.vm.$.vnode.props!.onClick as () => void; await state().load(); refresh(); select('stale'); run(); await flushPromises();
    expect(api.listPrintingTemplates).toHaveBeenCalledTimes(2); expect(api.getPrintingTemplateVersion).not.toHaveBeenCalled(); expect(state().selectedTemplateId).toBe(printTemplate.id);
  });
  it('旧新建输入、提交和关闭回调不能接入重开表单', async () => {
    await setup(); await prepareCreate(); const dialog = wrapper.findAllComponents(ElDialog)[0]!, input = dialog.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
    const close = dialog.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void, submit = dialog.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'printing-preview-submit')!.vm.$.vnode.props!.onClick as () => void;
    state().createDialogVisible = false; await prepareCreate(); input('stale'); close(false); submit(); await flushPromises(); expect(state().createForm.templateKey).not.toBe('stale'); expect(state().createDialogVisible).toBe(true); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
  });
  it('旧恢复确认不能解锁后续确认，确认本身不发请求', async () => {
    await setup(); await prepareCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(failure); await state().submitCreate(); state().acknowledgeRecovery(); await flushPromises(); const confirm = wrapper.getComponent(ArtFormDialog).vm.$.vnode.props!.onConfirm as () => void;
    state().recoveryPrompt!.close(false); state().acknowledgeRecovery(); confirm(); await flushPromises(); expect(state().createBlocked).toBe(true); state().recoveryPrompt!.close(false); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  });
  it('同模板重开授权窗口后，旧关闭回调不能关闭新窗口', async () => {
    await setup(); const open = () => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'printing-tenant-grants-open')!.vm.$emit('click'); open(); await flushPromises(); const first = wrapper.getComponent(GrantStub), oldClose = first.vm.$.vnode.props!.onClose as () => void;
    first.vm.$emit('close'); await flushPromises(); open(); await flushPromises(); oldClose(); await flushPromises(); expect(wrapper.findComponent(GrantStub).exists()).toBe(true);
  });
  it('离开路由取消正文并清空私有新建布局，晚结果不能重新插入DOM', async () => {
    const route = reactive({ name: 'printing-preview' }), pending = deferred<ReturnType<typeof hostPrintingVersion>>(); await setup(route); state().createForm.layoutHtml = '<p>私有草稿</p>';
    vi.mocked(api.getPrintingTemplateVersion).mockReturnValueOnce(pending.promise); const action = state().runPreview(), signal = vi.mocked(api.getPrintingTemplateVersion).mock.lastCall![2]!; route.name = 'home'; pending.resolve({ ...versionBody(printTemplate, 1), layoutHtml: '<p>旧版式</p>' }); await action;
    expect(state().preview).toBeUndefined(); expect(state().createForm.layoutHtml).not.toContain('私有草稿'); expect(wrapper.find('iframe').exists()).toBe(false); expect(signal.aborted).toBe(true);
  });
});
