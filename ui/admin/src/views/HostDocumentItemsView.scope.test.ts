import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDrawer, ElMessage } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import HostDocumentItemsView from './HostDocumentItemsView.vue';
import * as api from '../api/host-document-items';
import { listDocumentCategories } from '../api/host-document-categories';
import { listDocumentTags } from '../api/host-document-tags';
import { createDocumentPreviewTask } from '../api/document-preview-tasks';
import { createShareSession, shareDocument } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn(), createDocumentItem: vi.fn(), updateDocumentItem: vi.fn(),
  deleteDocumentItem: vi.fn(), restoreDocumentItem: vi.fn(), uploadDocumentVersion: vi.fn(), downloadDocumentContent: vi.fn(),
  previewDocumentContent: vi.fn(), openDocumentBlob: vi.fn(), listDocumentVersions: vi.fn(), rollbackDocumentVersion: vi.fn(), deleteDocumentVersion: vi.fn() }));
vi.mock('../api/host-document-categories', () => ({ listDocumentCategories: vi.fn() }));
vi.mock('../api/host-document-tags', () => ({ listDocumentTags: vi.fn() }));
vi.mock('../api/document-preview-tasks', () => ({ createDocumentPreviewTask: vi.fn() }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }));
const doc = { ...shareDocument, categoryIcon: null, categoryCode: null, shareCode: null, hasPassword: false, permissionLevel: null, permissions: [] };
const oldVersion = { ...doc.currentVersion, id: '01980000-0000-7000-8000-000000000003', versionNumber: 0, changeDescription: '旧版本敏感内容' };
const permissions = ['read', 'create', 'update', 'delete', 'restore', 'download', 'add_version', 'rollback_version', 'delete_version']
  .map(value => 'document.host_documents.' + value).concat(['document.host_preview_tasks.create', 'document.categories.read', 'document.tags.read']);
let wrapper: VueWrapper | undefined; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(api.listDocumentItems).mockResolvedValue({ items: [doc], page: 1, pageSize: 100, total: 1 });
  vi.mocked(listDocumentCategories).mockResolvedValue([]); vi.mocked(listDocumentTags).mockResolvedValue([]);
  vi.mocked(api.listDocumentVersions).mockResolvedValue([oldVersion]);
  vi.mocked(api.createDocumentItem).mockResolvedValue(doc); vi.mocked(api.updateDocumentItem).mockResolvedValue(doc);
  vi.mocked(api.uploadDocumentVersion).mockResolvedValue(doc); vi.mocked(api.rollbackDocumentVersion).mockResolvedValue(doc); vi.mocked(api.deleteDocumentVersion).mockResolvedValue(doc);
  vi.mocked(api.deleteDocumentItem).mockResolvedValue(true);
});
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
async function setup(granted = permissions, keepAlive = false) {
  const context = createShareSession(granted); visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(HostDocumentItemsView) : h(blank) }) });
  wrapper = mount(keepAlive ? root : HostDocumentItemsView, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' },
    ElDrawer: { name: 'ElDrawer', props: ['modelValue'], template: '<div v-if="modelValue"><slot/></div>' }
  } } }); await flushPromises(); return context.session;
}
async function call(name: string, ...args: unknown[]) {
  const vm = wrapper!.findComponent(HostDocumentItemsView).vm as unknown as Record<string, (...values: unknown[]) => unknown>;
  return await vm[name]!(...args);
}
async function startCreate() {
  await call('openCreate'); await wrapper!.get('[data-testid="host-document-item-title"]').setValue('New document');
}
async function confirmed(name: string, ...args: unknown[]) {
  const action = call(name, ...args); await flushPromises();
  await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await action;
}
describe('Host 文档库整页请求归属', () => {
  it('无文档读取权限不读取列表或附属目录', async () => {
    await setup([]); expect(api.listDocumentItems).not.toHaveBeenCalled(); expect(listDocumentTags).not.toHaveBeenCalled(); expect(listDocumentCategories).not.toHaveBeenCalled();
  });
  it('分类和标签读取各自要求精确权限', async () => {
    await setup(['document.host_documents.read']); expect(api.listDocumentItems).toHaveBeenCalledOnce();
    expect(listDocumentCategories).not.toHaveBeenCalled(); expect(listDocumentTags).not.toHaveBeenCalled();
  });
  it('切入租户后清除 Host 列表，不在租户作用域重读', async () => {
    const session = await setup(); session.currentUser = { ...session.currentUser!, tenantId: doc.id, scope: `tenant:${doc.id.replaceAll('-', '')}` };
    await flushPromises(); expect(wrapper!.text()).not.toContain(doc.title); expect(api.listDocumentItems).toHaveBeenCalledOnce();
  });
  it('撤权中止列表，迟到结果不能恢复旧文档', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listDocumentItems>>>(); vi.mocked(api.listDocumentItems).mockReturnValueOnce(pending.promise);
    const session = await setup(); const signal = vi.mocked(api.listDocumentItems).mock.calls[0]![3]!;
    session.currentUser!.permissions = []; pending.resolve({ items: [doc], page: 1, pageSize: 100, total: 1 }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).not.toContain(doc.title);
  });
  it('列表刷新取消旧读取，迟到页不能替换新页', async () => {
    const pending = deferred<Awaited<ReturnType<typeof api.listDocumentItems>>>(); vi.mocked(api.listDocumentItems).mockReturnValueOnce(pending.promise);
    await setup(); const signal = vi.mocked(api.listDocumentItems).mock.calls[0]![3]!; await call('load');
    pending.resolve({ items: [{ ...doc, title: '迟到旧文档' }], page: 1, pageSize: 100, total: 1 }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).not.toContain('迟到旧文档');
  });
  it.each(['download', 'preview'])('%s 撤权后迟到 Blob 不打开，错误也不写入新页面', async kind => {
    const pending = deferred<Blob>();
    if (kind === 'download') vi.mocked(api.downloadDocumentContent).mockReturnValueOnce(pending.promise); else vi.mocked(api.previewDocumentContent).mockReturnValueOnce(pending.promise);
    const session = await setup(); const action = kind === 'download' ? call('downloadFile', doc.id) : call('previewFile', doc);
    session.currentUser!.permissions = []; pending.resolve(new Blob(['private'])); await action; await flushPromises(); expect(api.openDocumentBlob).not.toHaveBeenCalled();
  });
  it('关闭版本抽屉取消读取，迟到版本不能重新展示', async () => {
    const pending = deferred<typeof oldVersion[]>(); vi.mocked(api.listDocumentVersions).mockReturnValueOnce(pending.promise); await setup();
    const action = call('openVersionHistory', doc); await flushPromises(); const signal = vi.mocked(api.listDocumentVersions).mock.calls[0]![1]!;
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await flushPromises(); pending.resolve([oldVersion]); await action;
    expect(signal.aborted).toBe(true); expect(wrapper!.text()).not.toContain('旧版本敏感内容');
  });
  it('取消创建后迟到冲突不能重新打开已关闭弹窗', async () => {
    const pending = deferred<typeof doc>(); vi.mocked(api.createDocumentItem).mockReturnValueOnce(pending.promise); await setup(); await startCreate();
    const action = call('create'); await call('cancelCreate'); pending.reject({ status: 409, code: 'document.host_document.title_conflict', title: 'conflict' }); await action; await flushPromises();
    expect(wrapper!.find('[data-testid="host-document-item-title"]').exists()).toBe(false);
  });
  it('关闭后重新创建时旧成功不能关闭新弹窗', async () => {
    const pending = deferred<typeof doc>(); vi.mocked(api.createDocumentItem).mockReturnValueOnce(pending.promise); await setup(); await startCreate();
    const action = call('create'); await call('cancelCreate'); await startCreate(); pending.resolve(doc); await action; await flushPromises();
    expect(wrapper!.find('[data-testid="host-document-item-title"]').exists()).toBe(true);
  });
  it.each(['create', 'edit', 'upload', 'office'])('%s 在撤权后不反馈成功或刷新新上下文', async kind => {
    const pending = deferred<typeof doc>();
    if (kind === 'create') vi.mocked(api.createDocumentItem).mockReturnValueOnce(pending.promise);
    if (kind === 'edit') vi.mocked(api.updateDocumentItem).mockReturnValueOnce(pending.promise);
    if (kind === 'upload') vi.mocked(api.uploadDocumentVersion).mockReturnValueOnce(pending.promise);
    // 只测试晚取消后返回，任务内容在拒绝接入后不应再被解释。
    const office = deferred<Awaited<ReturnType<typeof createDocumentPreviewTask>>>(); if (kind === 'office') vi.mocked(createDocumentPreviewTask).mockReturnValueOnce(office.promise);
    const session = await setup(); const success = vi.spyOn(ElMessage, 'success');
    if (kind === 'create') await startCreate(); if (kind === 'edit') await call('startEdit', doc);
    if (kind === 'upload') await call('onVersionFileSelected', { target: { files: [new File(['fixture'], 'doc.txt')] } }, doc.id);
    const action = kind === 'create' ? call('create') : kind === 'edit' ? call('saveEdit', doc) : kind === 'upload' ? call('uploadVersion', doc) : call('submitOfficePreviewTask', doc);
    session.currentUser!.permissions = []; pending.resolve(doc);
    if (kind === 'office') office.resolve({ id: doc.id, documentItemId: doc.id, documentTitle: doc.title, versionId: doc.currentVersion.id,
      sourceFileId: doc.currentVersion.fileId, outputFileId: null, statusKey: 'pending', providerKey: 'fixture', errorCode: null,
      requestedByUserId: doc.createdByUserId, createdAtUtc: doc.createdAtUtc, startedAtUtc: null, completedAtUtc: null, version: 1 });
    await action; await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(api.listDocumentItems).toHaveBeenCalledOnce();
  });
  it.each(['remove', 'rollbackVersion', 'deleteVersion'])('%s 确认等待中撤权不发写请求', async kind => {
    const session = await setup();
    if (kind !== 'remove') await call('openVersionHistory', doc);
    const action = call(kind, kind === 'remove' ? doc : oldVersion); session.currentUser!.permissions = []; await action; await flushPromises();
    expect(api.deleteDocumentItem).not.toHaveBeenCalled(); expect(api.rollbackDocumentVersion).not.toHaveBeenCalled(); expect(api.deleteDocumentVersion).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="host-document-confirm-message"]').exists()).toBe(false);
  });
  it('KeepAlive 停用清理页面，恢复只重载当前授权目录', async () => {
    await setup(permissions, true); visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(api.listDocumentItems).toHaveBeenCalledTimes(2); expect(listDocumentCategories).toHaveBeenCalledTimes(2); expect(listDocumentTags).toHaveBeenCalledTimes(4);
  });
  it('稳定会话创建保留原参数并刷新列表', async () => {
    await setup(); await startCreate(); await call('create'); await flushPromises();
    expect(api.createDocumentItem).toHaveBeenCalledWith('New document', null, { categoryId: null, tagIds: [] }, expect.any(AbortSignal));
    expect(api.listDocumentItems).toHaveBeenCalledTimes(2); expect(wrapper!.find('[data-testid="host-document-item-title"]').exists()).toBe(false);
  });
  it('稳定会话下载仍由认证 Blob 打开', async () => {
    const blob = new Blob(['allowed']); vi.mocked(api.downloadDocumentContent).mockResolvedValue(blob); await setup(); await call('downloadFile', doc.id);
    expect(api.openDocumentBlob).toHaveBeenCalledExactlyOnceWith(blob);
  });
  it('恢复已删除记录在撤权后不交付旧成功', async () => {
    const session = await setup(); await confirmed('remove', doc);
    const pending = deferred<typeof doc>(); vi.mocked(api.restoreDocumentItem).mockReturnValueOnce(pending.promise);
    const success = vi.spyOn(ElMessage, 'success'); success.mockClear();
    const action = call('restoreDeleted', { item: doc, restoreVersion: doc.version + 1 });
    expect(api.restoreDocumentItem).toHaveBeenCalledOnce(); session.currentUser!.permissions = []; pending.resolve(doc); await action; await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(api.listDocumentItems).toHaveBeenCalledTimes(2);
  });
  it.each(['rollbackVersion', 'deleteVersion'])('%s 在关闭抽屉后不接入迟到写结果或重读历史', async kind => {
    const pending = deferred<typeof doc>();
    if (kind === 'rollbackVersion') vi.mocked(api.rollbackDocumentVersion).mockReturnValueOnce(pending.promise); else vi.mocked(api.deleteDocumentVersion).mockReturnValueOnce(pending.promise);
    await setup(); await call('openVersionHistory', doc); const success = vi.spyOn(ElMessage, 'success'); const action = call(kind, oldVersion); await flushPromises();
    await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await flushPromises();
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await flushPromises(); pending.resolve(doc); await action;
    expect(success).not.toHaveBeenCalled(); expect(api.listDocumentVersions).toHaveBeenCalledOnce();
  });
  it('稳定会话回滚指定非当前版本后重读历史', async () => {
    await setup(); await call('openVersionHistory', doc); await confirmed('rollbackVersion', oldVersion);
    expect(api.rollbackDocumentVersion).toHaveBeenCalledWith(doc.id, oldVersion.id, doc.version, expect.any(AbortSignal));
    expect(api.listDocumentVersions).toHaveBeenCalledTimes(2);
  });
  it('放弃删除只结束本页确认，不发送删除', async () => {
    await setup(); const action = call('remove', doc); await flushPromises();
    await wrapper!.get('[data-testid="host-document-confirm-dismiss"]').trigger('click'); await action;
    expect(api.deleteDocumentItem).not.toHaveBeenCalled();
  });
  it('版本确认期间关闭抽屉同时移除确认并结束等待', async () => {
    await setup(); await call('openVersionHistory', doc); const action = call('rollbackVersion', oldVersion); await flushPromises();
    wrapper!.getComponent(ElDrawer).vm.$emit('update:modelValue', false); await action; await flushPromises();
    expect(api.rollbackDocumentVersion).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="host-document-confirm-message"]').exists()).toBe(false);
  });
  it('列表版本更新关闭旧编辑表单，不使用新版本提交旧输入', async () => {
    await setup(); await call('startEdit', doc);
    vi.mocked(api.listDocumentItems).mockResolvedValueOnce({ items: [{ ...doc, version: doc.version + 1 }], page: 1, pageSize: 100, total: 1 });
    await call('load'); await call('saveEdit', doc); await flushPromises();
    expect(api.updateDocumentItem).not.toHaveBeenCalled(); expect(wrapper!.find('[data-testid="host-document-item-edit-title"]').exists()).toBe(false);
  });
});
