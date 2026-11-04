import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElDialog, ElInput, ElMessage } from 'element-plus';
import View from './PrintingPreviewView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/printing-templates';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/printing-templates', () => ({ listPrintingTemplates: vi.fn(), createPrintingTemplate: vi.fn(), updatePrintingTemplate: vi.fn(), publishPrintingTemplate: vi.fn(),
  getPrintingTemplate: vi.fn(), getPrintingTemplateVersion: vi.fn(), listPrintingTemplateVersions: vi.fn(), getPrintingFormSchema: vi.fn() }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', actor = '019bc2b1-2a40-7cc3-8992-a80de51bf298', createdId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
const draft = { id, templateKey: 'existing', name: '当前草稿', formSchemaKey: 'printing.tenant_profile_card', layoutHtml: '<h1>Draft</h1>', latestPublishedVersionNumber: 1,
  isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 7 };
const { layoutHtml: _, ...summary } = draft;
const publication = { id: createdId, templateId: id, versionNumber: 2, layoutHtml: draft.layoutHtml, changeNote: null as string | null, publishedByUserId: actor, publishedAtUtc: draft.createdAtUtc };
let wrapper: VueWrapper;
interface State { createDialogVisible: boolean; publishOpen: boolean; createBlocked: boolean; createForm: { name: string }; writeReceipt?: { outcome: string; kind: string; templateId?: string; versionNumber?: number };
  openCreate: () => void; submitCreate: () => Promise<void>; confirmPublish: () => Promise<void>; publishSelected: () => Promise<void>; load: () => Promise<void> }
const state = () => wrapper.vm as unknown as State;
const button = (testId: string) => wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === testId)!;
beforeEach(() => {
  vi.resetAllMocks(); vi.spyOn(ElMessage, 'success').mockImplementation(() => ({ close() {} }));
  vi.mocked(api.listPrintingTemplates).mockResolvedValue({ items: [summary], page: 1, pageSize: 20, total: 1 });
  vi.mocked(api.listPrintingTemplateVersions).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 });
  vi.mocked(api.getPrintingTemplate).mockResolvedValue({ ...draft });
  vi.mocked(api.createPrintingTemplate).mockImplementation(async body => ({ ...draft, ...body, id: createdId, version: 1, latestPublishedVersionNumber: 0 }));
  vi.mocked(api.updatePrintingTemplate).mockImplementation(async (key, body) => ({ ...draft, ...body, id: key, version: body.version + 1 }));
  vi.mocked(api.publishPrintingTemplate).mockImplementation(async (_key, body) => ({ ...publication, changeNote: body.changeNote ?? null }));
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(extraPermissions: string[] = []) {
  const context = createShareSession([...['read', 'create', 'update', 'publish'].map(action => 'printing.templates.' + action), ...extraPermissions]); context.session.currentUser!.id = actor;
  wrapper = mount(View, { global: { plugins: [context.pinia], stubs: { ElDialog: { name: 'ElDialog', props: ['modelValue'], template: '<section v-if="modelValue"><slot/><slot name="footer"/></section>' },
    PrintingTenantGrantsDialog: { name: 'PrintingTenantGrantsDialog', props: ['template'], emits: ['close'], template: '<section data-testid="grant-window" />' } } } });
  await flushPromises(); return context.session;
}
async function openCreate() {
  await wrapper.get('[data-testid="printing-preview-create"]').trigger('click');
  await wrapper.get('[data-testid="printing-template-key"]').setValue('new-draft');
  await wrapper.get('[data-testid="printing-template-name"]').setValue('新草稿');
  await wrapper.get('[data-testid="printing-template-layout"]').setValue('<p>New draft</p>');
}
it('创建成功先固定草稿回执，目录补读失败不降级或自动发布', async () => {
  await setup(); await openCreate(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline')); await state().submitCreate();
  expect(state().writeReceipt).toMatchObject({ kind: 'create', outcome: 'succeeded', templateId: createdId }); expect(state().createDialogVisible).toBe(false);
  expect(api.publishPrintingTemplate).not.toHaveBeenCalled(); await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
});
it('明确创建拒绝保留表单，修正后由用户再次保存', async () => {
  await setup(); await openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce({ status: 400, code: 'fixture.invalid', title: 'Invalid' }); await state().submitCreate();
  expect(state().writeReceipt?.outcome).toBe('failed'); expect(state().createBlocked).toBe(false); expect(state().createDialogVisible).toBe(true);
  await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledTimes(2); expect(state().writeReceipt?.outcome).toBe('succeeded');
});
it('未知创建跨关闭与目录刷新保留保护，核对只解锁下一次手动保存', async () => {
  await setup(); await openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await state().submitCreate();
  state().createDialogVisible = false; await state().load(); state().openCreate(); await flushPromises();
  expect(state().createForm.name).toBe('新草稿'); await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
  await wrapper.get('[data-testid="printing-create-review"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="printing-create-review-confirm"]').trigger('click'); await flushPromises();
  expect(api.createPrintingTemplate).toHaveBeenCalledOnce(); expect(state().createBlocked).toBe(false);
  await state().submitCreate(); expect(api.createPrintingTemplate).toHaveBeenCalledTimes(2); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
});
it.each(['close', 'input'] as const)('创建在途%s取消接入并保留未知，迟到成功不污染新窗口', async kind => {
  await setup(); await openCreate(); const pending = deferred<typeof draft>(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise);
  const action = state().submitCreate(); const signal = vi.mocked(api.createPrintingTemplate).mock.lastCall![1]!;
  if (kind === 'close') { state().createDialogVisible = false; state().openCreate(); } else state().createForm.name = '后来输入';
  expect(signal.aborted).toBe(true); pending.resolve({ ...draft, id: createdId, version: 1, latestPublishedVersionNumber: 0 }); await action;
  expect(state().createBlocked).toBe(true); expect(state().writeReceipt?.outcome).toBe('unconfirmed'); expect(state().createDialogVisible).toBe(true); expect(ElMessage.success).not.toHaveBeenCalled();
});
it('旧创建窗口的输入、关闭和保存回调不能操作重开的表单', async () => {
  await setup(); await openCreate(); const input = wrapper.findAllComponents(ElDialog)[0]!.getComponent(ElInput);
  const oldInput = input.vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void;
  const oldSave = button('printing-preview-submit').vm.$.vnode.props!.onClick as () => void;
  const oldClose = wrapper.findAllComponents(ElDialog)[0]!.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
  state().createDialogVisible = false; state().openCreate(); await flushPromises(); oldInput('stale'); oldClose(false); oldSave(); await flushPromises();
  expect(state().createDialogVisible).toBe(true); expect(api.createPrintingTemplate).not.toHaveBeenCalled();
});
it('旧恢复确认不能解锁后续确认', async () => {
  await setup(); await openCreate(); vi.mocked(api.createPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await state().submitCreate();
  await wrapper.get('[data-testid="printing-create-review"]').trigger('click'); await flushPromises();
  const first = wrapper.getComponent(ArtFormDialog), confirm = first.vm.$.vnode.props!.onConfirm as () => void;
  first.vm.$emit('update:open', false); await flushPromises(); await wrapper.get('[data-testid="printing-create-review"]').trigger('click'); await flushPromises(); confirm();
  expect(state().createBlocked).toBe(true); expect(api.createPrintingTemplate).toHaveBeenCalledOnce();
});
it('新鲜发布说明使旧确认失效，新确认发送对应说明并保存精确版本', async () => {
  await setup(); await state().confirmPublish(); await flushPromises(); const old = button('printing-template-confirm-publish').vm.$.vnode.props!.onClick as () => void;
  await wrapper.get('[data-testid="printing-template-change-note"]').setValue('Reviewed'); old(); await flushPromises(); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
  await wrapper.get('[data-testid="printing-template-confirm-publish"]').trigger('click'); await flushPromises();
  expect(api.publishPrintingTemplate).toHaveBeenCalledWith(id, { version: 7, changeNote: 'Reviewed' }, expect.any(AbortSignal));
  expect(state().writeReceipt).toMatchObject({ kind: 'publish', outcome: 'succeeded', versionNumber: 2 }); expect(state().publishOpen).toBe(false);
});
it('旧发布窗口的关闭和确认回调不能批准新窗口', async () => {
  await setup(); await state().confirmPublish(); await flushPromises(); const old = button('printing-template-confirm-publish').vm.$.vnode.props!.onClick as () => void;
  const close = wrapper.findAllComponents(ElDialog)[1]!.vm.$.vnode.props!['onUpdate:modelValue'] as (open: boolean) => void;
  close(false); await state().confirmPublish(); await flushPromises(); close(false); old(); await flushPromises();
  expect(state().publishOpen).toBe(true); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
});
it.each([{ templateId: createdId }, { publishedByUserId: createdId }, { versionNumber: 1 }, { layoutHtml: '<p>Other</p>' }])('错配发布回包%j不能报成功或刷新', async change => {
  await setup(); vi.mocked(api.publishPrintingTemplate).mockResolvedValueOnce({ ...publication, ...change }); await state().confirmPublish(); await state().publishSelected();
  expect(state().writeReceipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled(); expect(api.listPrintingTemplates).toHaveBeenCalledOnce();
});
it('发布成功后的目录补读失败保留已确认版本，不自动再次发布', async () => {
  await setup(); await state().confirmPublish(); vi.mocked(api.listPrintingTemplates).mockRejectedValueOnce(new Error('offline')); await state().publishSelected();
  expect(state().writeReceipt).toMatchObject({ outcome: 'succeeded', versionNumber: 2 }); await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
});
it('已确认发布的修订先更新本地，陈旧目录不能回退且不重新开放发布', async () => {
  await setup(); await state().confirmPublish(); await state().publishSelected();
  const vm = wrapper.vm as unknown as { templates: { version: number; latestPublishedVersionNumber: number }[]; catalogReady: boolean };
  expect(vm.templates[0]).toMatchObject({ version: 8, latestPublishedVersionNumber: 2 }); expect(vm.catalogReady).toBe(false);
  await state().load(); expect(vm.templates[0]).toMatchObject({ version: 8, latestPublishedVersionNumber: 2 }); await state().confirmPublish(); await state().publishSelected();
  expect(api.publishPrintingTemplate).toHaveBeenCalledOnce(); expect(state().writeReceipt).toMatchObject({ outcome: 'succeeded', versionNumber: 2 });
});
it('关闭在途发布取消旧响应，晚成功不覆盖未知回执', async () => {
  await setup(); await state().confirmPublish(); const pending = deferred<typeof publication>(); vi.mocked(api.publishPrintingTemplate).mockReturnValueOnce(pending.promise);
  const action = state().publishSelected(); const signal = vi.mocked(api.publishPrintingTemplate).mock.lastCall![2]!; state().publishOpen = false;
  expect(signal.aborted).toBe(true); pending.resolve(publication); await action; expect(state().writeReceipt?.outcome).toBe('unconfirmed'); expect(ElMessage.success).not.toHaveBeenCalled();
});
it('编辑保存使用原目标版本，保存成功仍不自动发布', async () => {
  await setup(); await wrapper.get('[data-testid="printing-template-edit"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="printing-template-name"]').setValue('更新草稿');
  await state().submitCreate(); expect(api.updatePrintingTemplate).toHaveBeenCalledWith(id, { name: '更新草稿', layoutHtml: draft.layoutHtml, isEnabled: true, version: 7 }, expect.any(AbortSignal));
  expect(state().writeReceipt).toMatchObject({ kind: 'update', outcome: 'succeeded', templateId: id }); expect(api.publishPrintingTemplate).not.toHaveBeenCalled();
});
it('写入期间会话变化销毁回执并取消请求', async () => {
  const session = await setup(); await openCreate(); const pending = deferred<typeof draft>(); vi.mocked(api.createPrintingTemplate).mockReturnValueOnce(pending.promise);
  const action = state().submitCreate(); const signal = vi.mocked(api.createPrintingTemplate).mock.lastCall![1]!; session.currentUser!.sessionId = createdId;
  expect(signal.aborted).toBe(true); pending.resolve(draft); await action; expect(state().writeReceipt).toBeUndefined(); expect(ElMessage.success).not.toHaveBeenCalled();
});
it('未知发布按模板保留，切换模板和覆盖回执不能解锁原模板', async () => {
  const other = { ...draft, id: createdId, templateKey: 'other' }; vi.mocked(api.listPrintingTemplates).mockResolvedValue({ items: [summary, { ...summary, id: createdId, templateKey: 'other' }], total: 2, page: 1, pageSize: 20 });
  await setup(); vi.mocked(api.getPrintingTemplate).mockImplementation(async target => target === createdId ? other : draft); vi.mocked(api.publishPrintingTemplate).mockRejectedValue(new Error('lost'));
  await state().confirmPublish(); await state().publishSelected(); const vm = wrapper.vm as unknown as { selectedTemplateId: string; publicationBlocks: Map<string, number> };
  vm.selectedTemplateId = createdId; await state().confirmPublish(); await state().publishSelected(); vm.selectedTemplateId = id;
  await state().confirmPublish(); await state().publishSelected(); expect(api.publishPrintingTemplate).toHaveBeenCalledTimes(2); expect(vm.publicationBlocks.get(id)).toBe(7);
  await state().load(); expect(vm.publicationBlocks.get(id)).toBe(7);
});
it('旧发布核对确认不能解锁后续确认，确认本身不发请求', async () => {
  await setup(); vi.mocked(api.publishPrintingTemplate).mockRejectedValueOnce(new Error('lost')); await state().confirmPublish(); await state().publishSelected(); await state().load();
  await wrapper.get('[data-testid="printing-publish-review"]').trigger('click'); await flushPromises();
  const first = wrapper.getComponent(ArtFormDialog), confirm = first.vm.$.vnode.props!.onConfirm as () => void; first.vm.$emit('update:open', false); await flushPromises();
  await wrapper.get('[data-testid="printing-publish-review"]').trigger('click'); await flushPromises(); confirm();
  expect((wrapper.vm as unknown as { publicationBlocks: Map<string, number> }).publicationBlocks.has(id)).toBe(true);
  await wrapper.get('[data-testid="printing-publish-review-confirm"]').trigger('click'); await flushPromises();
  expect((wrapper.vm as unknown as { publicationBlocks: Map<string, number> }).publicationBlocks.has(id)).toBe(false); expect(api.publishPrintingTemplate).toHaveBeenCalledOnce();
});
it('授权窗口冻结已发布模板，同模板重开后旧关闭不能关闭新窗口，撤权销毁窗口', async () => {
  const session = await setup(['printing.templates.grant_tenants']); await wrapper.get('[data-testid="printing-tenant-grants-open"]').trigger('click'); await flushPromises();
  const first = wrapper.getComponent({ name: 'PrintingTenantGrantsDialog' }), oldClose = first.vm.$.vnode.props!.onClose as () => void;
  expect(first.props('template')).toEqual({ id, name: draft.name, latestPublishedVersionNumber: 1 }); first.vm.$emit('close'); await flushPromises();
  await wrapper.get('[data-testid="printing-tenant-grants-open"]').trigger('click'); await flushPromises(); oldClose(); await flushPromises(); expect(wrapper.find('[data-testid="grant-window"]').exists()).toBe(true);
  session.currentUser!.permissions = ['printing.templates.read']; await flushPromises(); expect(wrapper.find('[data-testid="grant-window"]').exists()).toBe(false);
});
