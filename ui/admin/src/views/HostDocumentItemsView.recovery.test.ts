import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElDrawer, ElMessage } from 'element-plus';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import HostDocumentItemsView from './HostDocumentItemsView.vue';
import * as api from '../api/host-document-items';
import { listDocumentCategories } from '../api/host-document-categories';
import { listDocumentTags } from '../api/host-document-tags';
import { createDocumentPreviewTask } from '../api/document-preview-tasks';
import { documentPreviewTask } from '../test/task-lifecycle-fixtures';
import { createShareSession, shareDocument } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn(), createDocumentItem: vi.fn(), updateDocumentItem: vi.fn(),
  deleteDocumentItem: vi.fn(), restoreDocumentItem: vi.fn(), uploadDocumentVersion: vi.fn(), downloadDocumentContent: vi.fn(),
  previewDocumentContent: vi.fn(), openDocumentBlob: vi.fn(), listDocumentVersions: vi.fn(), rollbackDocumentVersion: vi.fn(), deleteDocumentVersion: vi.fn() }));
vi.mock('../api/host-document-categories', () => ({ listDocumentCategories: vi.fn() }));
vi.mock('../api/host-document-tags', () => ({ listDocumentTags: vi.fn() }));
vi.mock('../api/document-preview-tasks', () => ({ createDocumentPreviewTask: vi.fn() }));
const { routerPush } = vi.hoisted(() => ({ routerPush: vi.fn() }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: routerPush }) }));
const doc = { ...shareDocument, categoryIcon: null, categoryCode: null, shareCode: null, hasPassword: false, permissionLevel: null, permissions: [] };
const oldVersion = { ...doc.currentVersion, id: '01980000-0000-7000-8000-000000000003', versionNumber: 2, changeDescription: '旧版本敏感内容' };
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
async function confirmed(name: string, ...args: unknown[]) {
  const action = call(name, ...args); await flushPromises();
  await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await action;
}
describe('Host 文档删除恢复与历史版本确认结果', () => {
  it('仅有子操作权限不能直接创建或读取文档', async () => {
    await setup(permissions.filter(value => value !== 'document.host_documents.read')); await call('openCreate'); await call('create');
    expect(api.listDocumentItems).not.toHaveBeenCalled(); expect(api.createDocumentItem).not.toHaveBeenCalled();
  });
  it('删除false不报告成功、不生成恢复记录或刷新', async () => {
    await setup(); vi.mocked(api.deleteDocumentItem).mockResolvedValueOnce(false); const success = vi.spyOn(ElMessage, 'success');
    await confirmed('remove', doc); expect(success).not.toHaveBeenCalled(); expect(api.listDocumentItems).toHaveBeenCalledOnce();
    expect((wrapper!.vm as unknown as { recentlyDeleted: unknown[] }).recentlyDeleted).toEqual([]);
  });
  it('确认删除后补读失败仍移除本地行并保留原恢复凭据', async () => {
    await setup(); vi.mocked(api.listDocumentItems).mockRejectedValueOnce(new Error('offline')); await confirmed('remove', doc);
    const state = wrapper!.vm as unknown as { items: typeof doc[]; recentlyDeleted: { item: typeof doc; restoreVersion: number }[] };
    expect(state.items).toEqual([]); expect(state.recentlyDeleted[0]).toMatchObject({ item: { id: doc.id }, restoreVersion: doc.version + 1 });
  });
  it('确认期间原对象就地升版不能偷用新版本删除', async () => {
    const original = { ...doc }; vi.mocked(api.listDocumentItems).mockResolvedValueOnce({ items: [original], page: 1, pageSize: 100, total: 1 }); await setup();
    const action = call('remove', original); await flushPromises(); original.version += 1;
    await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await action;
    expect(api.deleteDocumentItem).not.toHaveBeenCalled();
  });
  it('UUID大小写等价删除使用权威目录标识和标题', async () => {
    await setup(); await confirmed('remove', { ...doc, id: doc.id.toUpperCase(), title: '伪造标题' });
    expect(api.deleteDocumentItem).toHaveBeenCalledExactlyOnceWith(doc.id, doc.version, expect.any(AbortSignal));
  });
  it('删除晚确认不能移除已观察到更高修订的当前文档', async () => {
    await setup(); const pending = deferred<boolean>(); vi.mocked(api.deleteDocumentItem).mockReturnValueOnce(pending.promise);
    const action = call('remove', doc); await flushPromises(); await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click');
    vi.mocked(api.listDocumentItems).mockResolvedValueOnce({ items: [{ ...doc, version: doc.version + 2 }], page: 1, pageSize: 100, total: 1 }); await call('load');
    vi.mocked(api.listDocumentItems).mockRejectedValueOnce(new Error('offline')); pending.resolve(true); await action;
    const state = wrapper!.vm as unknown as { items: typeof doc[]; recentlyDeleted: unknown[] }; expect(state.items[0]?.version).toBe(doc.version + 2); expect(state.recentlyDeleted).toEqual([]);
  });
  it('旧确认监听器不能批准或关闭下一次确认', async () => {
    await setup(); const first = call('remove', doc); await flushPromises();
    const accept = wrapper!.findAllComponents({ name: 'ElButton' }).find(item => item.attributes('data-testid') === 'host-document-confirm-accept')!.vm.$.vnode.props!.onClick as () => void;
    const dismiss = wrapper!.findAllComponents({ name: 'ElButton' }).find(item => item.attributes('data-testid') === 'host-document-confirm-dismiss')!.vm.$.vnode.props!.onClick as () => void;
    await wrapper!.get('[data-testid="host-document-confirm-dismiss"]').trigger('click'); await first;
    const second = call('remove', doc); await flushPromises(); accept(); dismiss(); await flushPromises(); expect(api.deleteDocumentItem).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="host-document-confirm-accept"]').exists()).toBe(true);
    await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await second; expect(api.deleteDocumentItem).toHaveBeenCalledOnce();
  });
  it('恢复使用自有记录凭据，补读失败仍保留权威恢复结果', async () => {
    await setup(); await confirmed('remove', doc); const restored = { ...doc, title: '已恢复', version: doc.version + 2 };
    vi.mocked(api.restoreDocumentItem).mockResolvedValueOnce(restored); vi.mocked(api.listDocumentItems).mockRejectedValueOnce(new Error('offline'));
    await call('restoreDeleted', { item: { ...doc, id: doc.id.toUpperCase(), title: '事件名称' }, restoreVersion: doc.version + 1 });
    expect(api.restoreDocumentItem).toHaveBeenCalledExactlyOnceWith(doc.id, doc.version + 1, expect.any(AbortSignal));
    const state = wrapper!.vm as unknown as { items: typeof doc[]; recentlyDeleted: unknown[] }; expect(state.recentlyDeleted).toEqual([]); expect(state.items[0]).toEqual(restored);
  });
  it('恢复错配任务身份不清除自有恢复记录', async () => {
    await setup(); await confirmed('remove', doc); vi.mocked(api.restoreDocumentItem).mockResolvedValueOnce({ ...doc, id: oldVersion.id }); const success = vi.spyOn(ElMessage, 'success'); success.mockClear();
    await call('restoreDeleted', { item: doc, restoreVersion: doc.version + 1 }); expect(success).not.toHaveBeenCalled();
    expect((wrapper!.vm as unknown as { recentlyDeleted: unknown[] }).recentlyDeleted).toHaveLength(1);
  });
  it('更新目录修订会关闭旧历史抽屉并取消其读取', async () => {
    await setup(); const pending = deferred<typeof oldVersion[]>(); vi.mocked(api.listDocumentVersions).mockReturnValueOnce(pending.promise); const opening = call('openVersionHistory', doc); const signal = vi.mocked(api.listDocumentVersions).mock.calls[0]?.[1];
    vi.mocked(api.listDocumentItems).mockResolvedValueOnce({ items: [{ ...doc, version: doc.version + 1 }], page: 1, pageSize: 100, total: 1 }); await call('load'); expect(signal?.aborted).toBe(true);
    pending.resolve([oldVersion]); await opening; expect(wrapper!.getComponent(ElDrawer).props('modelValue')).toBe(false);
  });
  it.each(['rollbackVersion', 'deleteVersion'])('%s确认冻结原版本标识，不接受在途行对象改写', async kind => {
    await setup(); const version = { ...oldVersion }; vi.mocked(api.listDocumentVersions).mockResolvedValueOnce([version]); await call('openVersionHistory', doc);
    const action = call(kind, version); await flushPromises(); version.id = doc.currentVersion.id;
    await wrapper!.get('[data-testid="host-document-confirm-accept"]').trigger('click'); await action;
    expect(api.rollbackDocumentVersion).not.toHaveBeenCalled(); expect(api.deleteDocumentVersion).not.toHaveBeenCalled();
  });
  it.each(['rollbackVersion', 'deleteVersion'])('%s确认成功但重读失败仍报告已确认写入并保留新文档', async kind => {
    await setup(); const updated = { ...doc, version: doc.version + 1, currentVersion: kind === 'rollbackVersion' ? oldVersion : doc.currentVersion };
    if (kind === 'rollbackVersion') vi.mocked(api.rollbackDocumentVersion).mockResolvedValueOnce(updated); else vi.mocked(api.deleteDocumentVersion).mockResolvedValueOnce(updated);
    await call('openVersionHistory', doc); vi.mocked(api.listDocumentVersions).mockRejectedValueOnce(new Error('history offline')); const success = vi.spyOn(ElMessage, 'success');
    await confirmed(kind, oldVersion); expect(success).toHaveBeenCalledOnce(); expect((wrapper!.vm as unknown as { items: typeof doc[] }).items[0]?.version).toBe(doc.version + 1);
    if (kind === 'deleteVersion') expect((wrapper!.vm as unknown as { versionHistory: typeof oldVersion[] }).versionHistory).toEqual([]);
  });
  it('大小写等价历史版本使用目录中的原标识', async () => {
    await setup(); await call('openVersionHistory', { ...doc, id: doc.id.toUpperCase() }); await confirmed('deleteVersion', { ...oldVersion, id: oldVersion.id.toUpperCase() });
    expect(api.deleteDocumentVersion).toHaveBeenCalledExactlyOnceWith(doc.id, oldVersion.id, doc.version, expect.any(AbortSignal));
  });
});
