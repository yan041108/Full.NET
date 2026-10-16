import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reactive } from 'vue';
import { routeLocationKey } from 'vue-router';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElButton, ElInput, ElPagination } from 'element-plus';
import View from './DocumentPermissionsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import * as documents from '../api/host-document-items';
import * as acl from '../api/document-permissions';
import { createShareSession } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
import { permissionDocument as doc, permissionEntry as entry, otherPermissionEntry as other, permissionSnapshot as snapshot, permissionDocumentPage as page } from '../test/document-permission-fixtures';
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
vi.mock('../api/document-permissions', () => ({ getDocumentPermissionSnapshot: vi.fn(), setDocumentPermissions: vi.fn() }));
const granted = ['document.host_documents.read', 'document.host_permissions.read', 'document.host_permissions.set'];
const failure = { status: 503, title: '服务暂时不可用', code: 'fixture.unavailable' };
let wrapper: VueWrapper;
interface State {
  items: typeof doc[]; permissions: typeof entry[]; page: number; pageSize: number; catalogReady: boolean; permissionDialogOpen: boolean; permissionsReady: boolean; permissionsLoading: boolean; saving: boolean; userId: string; permissionLevel: string; permissionVersion: number;
  permissionProblem?: unknown; saveReceipt?: { documentId: string; outcome: string; version?: number };
  openPermissionDialog: (row: typeof doc) => Promise<void>; closePermissionDialog: () => void; reloadPermissions: () => Promise<void>; loadDocuments: () => Promise<void>;
  savePermission: () => Promise<void>; removePermission: (row: typeof entry) => Promise<void>; finishRemoval: (confirmed: boolean) => void; acknowledgeRecovery: () => Promise<void>; finishRecovery: (confirmed: boolean) => void; changePage: (page: number, size: number) => void;
}
const state = () => wrapper.vm as unknown as State;
async function setup(route?: { name: string }) { const context = createShareSession(granted); wrapper = mount(View, { global: { plugins: [context.pinia], provide: route ? { [routeLocationKey as symbol]: route } : {} } }); await flushPromises(); return context.session; }
async function open() { await state().openPermissionDialog(state().items[0]!); await flushPromises(); }
function input() { state().userId = other.userId; state().permissionLevel = 'read'; }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(documents.listDocumentItems).mockResolvedValue(page()); vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValue(snapshot()); vi.mocked(acl.setDocumentPermissions).mockImplementation(async body => body.permissions.map((row, index) => ({ ...entry, ...row, documentId: body.documentId, id: index === 0 ? entry.id : other.id }))); });
afterEach(() => { wrapper?.unmount(); vi.restoreAllMocks(); });
describe('文档权限整份替换与恢复流程', () => {
  it('目录失败保留同页行但暂停新读取和写入，重新读取成功后恢复', async () => {
    await setup(); vi.mocked(documents.listDocumentItems).mockRejectedValueOnce(failure); await state().loadDocuments(); expect(state().items).toHaveLength(1); expect(state().catalogReady).toBe(false);
    await open(); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled(); await state().loadDocuments(); await open(); expect(state().permissionsReady).toBe(true);
  });
  it('快照失败保留可关闭弹窗和重试，同修订重读保留输入', async () => {
    await setup(); await open(); input(); vi.mocked(acl.getDocumentPermissionSnapshot).mockRejectedValueOnce(failure); await state().reloadPermissions(); expect(state().permissionDialogOpen).toBe(true); expect(state().permissionProblem).toBeDefined(); expect(state().permissions).toHaveLength(2); expect(state().permissionsReady).toBe(false);
    await state().reloadPermissions(); expect(state().permissionsReady).toBe(true); expect(state().userId).toBe(other.userId); expect(state().permissionLevel).toBe('read');
  });
  it('关闭重开不能接入更低修订或同修订不同集合', async () => {
    await setup(); await open(); state().closePermissionDialog(); vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([], 6)); await open(); expect(state().permissionsReady).toBe(false);
    vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([], 7)); await state().reloadPermissions(); expect(state().permissionsReady).toBe(false); expect(state().permissionProblem).toBeDefined();
  });
  it('同修订目录刷新不丢手工草稿，但读取未成功时不能保存', async () => {
    await setup(); await open(); input(); const pending = deferred<ReturnType<typeof page>>(); vi.mocked(documents.listDocumentItems).mockReturnValueOnce(pending.promise); const read = state().loadDocuments(); await state().savePermission(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled(); pending.resolve(page()); await read; expect(state().userId).toBe(other.userId);
  });
  it('相同修订快照只改变排列顺序时仍可接入并保留草稿', async () => {
    await setup(); await open(); input(); vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([other, entry])); await state().reloadPermissions(); expect(state().permissionsReady).toBe(true); expect(state().userId).toBe(other.userId);
  });
  it.each([{ page: 2 }, { pageSize: 50 }, { items: [doc, { ...doc, id: doc.id.toUpperCase() }] }])('错页或重复文档目录 %j 不能成为保存依据', async change => {
    await setup(); vi.mocked(documents.listDocumentItems).mockResolvedValueOnce({ ...page(), ...change }); await state().loadDocuments(); expect(state().catalogReady).toBe(false); expect(state().items).toHaveLength(1); await open(); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled();
  });
  it('保存成功回执和新修订先接入，再次旧快照不能回退', async () => {
    await setup(); await open(); input(); await state().savePermission(); expect(state().saveReceipt).toMatchObject({ documentId: doc.id, outcome: 'succeeded', version: 8 }); expect(state().permissionVersion).toBe(8); expect(state().items[0]?.version).toBe(8);
    vi.mocked(acl.getDocumentPermissionSnapshot).mockRejectedValueOnce(failure); await state().reloadPermissions(); expect(state().saveReceipt?.outcome).toBe('succeeded'); vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot()); await state().reloadPermissions(); expect(state().permissionsReady).toBe(false);
  });
  it('未知写入关闭重开，同修订重读仍不能再次整份替换', async () => {
    await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce(failure); await state().savePermission(); state().closePermissionDialog(); await open(); input(); await state().savePermission(); expect(acl.setDocumentPermissions).toHaveBeenCalledOnce(); expect(state().saveReceipt?.outcome).toBe('unconfirmed');
  });
  it('更高修订读取解除旧 CAS 保护，但不能冒称原未知操作成功', async () => {
    await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce(failure); await state().savePermission(); vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([entry], 9)); await state().reloadPermissions();
    expect(state().saveReceipt?.outcome).toBe('unconfirmed'); input(); await state().savePermission(); expect(acl.setDocumentPermissions).toHaveBeenCalledTimes(2); expect(vi.mocked(acl.setDocumentPermissions).mock.calls[1]![0].version).toBe(9);
  });
  it('人工核对仅解锁手动操作，旧确认不能解锁后来的恢复窗口', async () => {
    await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce(failure); await state().savePermission(); await state().reloadPermissions();
    const first = state().acknowledgeRecovery(); await flushPromises(); const old = wrapper.findAllComponents(ArtFormDialog).find(dialog => dialog.props('confirmTestId') === 'document-permissions-recovery-confirm')!.vm.$.vnode.props!.onConfirm as () => void;
    state().finishRecovery(false); await first; const second = state().acknowledgeRecovery(); old(); await flushPromises(); input(); await state().savePermission(); expect(acl.setDocumentPermissions).toHaveBeenCalledOnce(); state().finishRecovery(true); await second; expect(acl.setDocumentPermissions).toHaveBeenCalledOnce();
  });
  it('明确 409 保留输入和失败回执，成功重读后可手动再保存', async () => {
    await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce({ status: 409, title: '修订冲突', code: 'document.host_permission.version_conflict' }); await state().savePermission(); expect(state().saveReceipt?.outcome).toBe('failed'); expect(state().userId).toBe(other.userId);
    await state().reloadPermissions(); await state().savePermission(); expect(acl.setDocumentPermissions).toHaveBeenCalledTimes(2);
  });
  it('写入响应缺项或重复行不接入成功状态', async () => {
    await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockResolvedValueOnce([entry]); await state().savePermission(); expect(state().saveReceipt?.outcome).toBe('unconfirmed'); expect(state().permissionsReady).toBe(false);
  });
  it('移除保存空集合仍保留新修订，后续新增不会复用旧版本', async () => {
    vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([entry])); await setup(); await open(); const remove = state().removePermission(entry); state().finishRemoval(true); await remove; expect(state().permissions).toHaveLength(0); expect(state().permissionVersion).toBe(8); input(); await state().savePermission(); expect(vi.mocked(acl.setDocumentPermissions).mock.calls[1]![0].version).toBe(8);
  });
  it('旧弹窗关闭、输入、提交和重读回调不能操作重开的窗口', async () => {
    await setup(); await open(); const dialog = wrapper.getComponent(ArtFormDialog), close = dialog.vm.$.vnode.props!['onUpdate:open'] as (value: boolean) => void, save = dialog.vm.$.vnode.props!.onConfirm as () => void;
    const change = dialog.getComponent(ElInput).vm.$.vnode.props!['onUpdate:modelValue'] as (value: string) => void, retry = dialog.findAllComponents(ElButton).find(button => button.attributes('data-testid') === 'document-permissions-load')!.vm.$.vnode.props!.onClick as () => void;
    state().closePermissionDialog(); await open(); close(false); change(other.userId); save(); retry(); await flushPromises(); expect(state().permissionDialogOpen).toBe(true); expect(state().userId).toBe(''); expect(acl.setDocumentPermissions).not.toHaveBeenCalled(); expect(acl.getDocumentPermissionSnapshot).toHaveBeenCalledTimes(2);
  });
  it('旧移除确认不能确认后来打开的移除提示', async () => {
    await setup(); await open(); const first = state().removePermission(entry); await flushPromises(); const old = wrapper.findAllComponents(ArtFormDialog).find(dialog => dialog.props('confirmTestId') === 'document-permissions-remove-confirm')!.vm.$.vnode.props!.onConfirm as () => void;
    state().finishRemoval(false); await first; const second = state().removePermission(other); old(); await flushPromises(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled(); state().finishRemoval(false); await second;
  });
  it('快照重读后旧移除按钮不能借同一用户的新配置', async () => {
    await setup(); await open(); const old = wrapper.findAllComponents(ArtTableActionButton).find(button => button.props('testId') === 'document-permissions-remove')!.vm.$.vnode.props!.onClick as () => void;
    await state().reloadPermissions(); old(); await flushPromises(); expect(state().saving).toBe(false); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
  });
  it('关闭已发送保存保留未知回执，迟到结果与 finally 不能改新窗口', async () => {
    const pending = deferred<typeof entry[]>(); await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockReturnValueOnce(pending.promise); const old = state().savePermission(); state().closePermissionDialog(); await open(); pending.resolve([entry, other]); await old; expect(state().permissionVersion).toBe(7); expect(state().saveReceipt?.outcome).toBe('unconfirmed');
  });
  it('保存发送后直接改输入取消旧接入并保留未知保护', async () => {
    const pending = deferred<typeof entry[]>(); await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockReturnValueOnce(pending.promise); const action = state().savePermission(); state().userId = entry.userId;
    pending.resolve([entry, { ...other, permissionLevel: 'read' }]); await action; expect(state().saveReceipt?.outcome).toBe('unconfirmed'); expect(state().permissionsReady).toBe(false); expect(vi.mocked(acl.setDocumentPermissions).mock.calls[0]![1]!.aborted).toBe(true);
  });
  it('切会话后旧保存 finally 不能清除新会话保存的忙状态', async () => {
    const first = deferred<typeof entry[]>(), next = deferred<typeof entry[]>(); const session = await setup(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockReturnValueOnce(first.promise); const old = state().savePermission();
    session.currentUser!.sessionId = other.userId; await flushPromises(); await open(); input(); vi.mocked(acl.setDocumentPermissions).mockReturnValueOnce(next.promise); const current = state().savePermission(); first.resolve([entry]); await old; expect(state().saving).toBe(true);
    next.resolve([entry, { ...other, permissionLevel: 'read' }]); await current; expect(state().saveReceipt?.outcome).toBe('succeeded'); expect(acl.setDocumentPermissions).toHaveBeenCalledTimes(2);
  });
  it('目录旧行回调不能打开刷新后的文档，目录更低修订不能覆盖新行', async () => {
    await setup(); const old = wrapper.findAllComponents(ArtTableActionButton).find(button => button.props('testId') === 'document-permissions-set')!.vm.$.vnode.props!.onClick as () => void;
    vi.mocked(documents.listDocumentItems).mockResolvedValueOnce(page([{ ...doc, version: 9 }])); await state().loadDocuments(); old(); await flushPromises(); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled(); await state().loadDocuments(); expect(state().catalogReady).toBe(false); expect(state().items[0]?.version).toBe(9);
  });
  it('真实分页器等待新页时不压回第一页，大小改变只读取第一页一次', async () => {
    vi.mocked(documents.listDocumentItems).mockResolvedValueOnce(page([doc], 1, 20, 60)); await setup(); const pending = deferred<ReturnType<typeof page>>(); vi.mocked(documents.listDocumentItems).mockReturnValueOnce(pending.promise); const pagination = wrapper.getComponent(ElPagination);
    pagination.vm.$emit('update:current-page', 2); pagination.vm.$emit('change', 2, 20); await flushPromises(); expect(state().page).toBe(2); expect(vi.mocked(documents.listDocumentItems).mock.calls[1]![3]!.aborted).toBe(false); pending.resolve(page([doc], 2, 20, 60)); await flushPromises();
    vi.mocked(documents.listDocumentItems).mockResolvedValueOnce(page([doc], 1, 50)); pagination.vm.$emit('update:page-size', 50); pagination.vm.$emit('change', 2, 50); await flushPromises(); expect(state().page).toBe(1); expect(documents.listDocumentItems).toHaveBeenCalledTimes(3);
  });
  it('离开路由取消快照读取并清除输入，旧结果不能恢复配置', async () => {
    const route = reactive({ name: 'document-permissions' }), pending = deferred<ReturnType<typeof snapshot>>(); await setup(route); vi.mocked(acl.getDocumentPermissionSnapshot).mockReturnValueOnce(pending.promise); const action = state().openPermissionDialog(doc); route.name = 'home'; pending.resolve(snapshot()); await action; expect(state().permissionDialogOpen).toBe(false); expect(state().permissions).toHaveLength(0); expect(vi.mocked(acl.getDocumentPermissionSnapshot).mock.calls[0]![1]!.aborted).toBe(true);
  });
});
