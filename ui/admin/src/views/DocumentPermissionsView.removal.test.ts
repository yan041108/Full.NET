import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import View from './DocumentPermissionsView.vue';
import * as documents from '../api/host-document-items';
import * as acl from '../api/document-permissions';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, shareDocument as doc, shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
vi.mock('../api/document-permissions', () => ({ getDocumentPermissionSnapshot: vi.fn(), setDocumentPermissions: vi.fn() }));
const read = 'document.host_documents.read', aclRead = 'document.host_permissions.read', set = 'document.host_permissions.set';
const permissions = [read, aclRead, set];
const entry = { id: shareId, documentId: doc.id, userId: shareId, permissionLevel: 'read', createdAtUtc: '2026-10-16T00:00:00Z' };
const other = { ...entry, id: otherShareId, userId: otherShareId, permissionLevel: 'write' };
const snapshot = (entries = [entry, other], version = 7) => ({ documentId: doc.id, version, permissions: entries });
let wrapper: VueWrapper | undefined; let visible = ref(true);
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(documents.listDocumentItems).mockResolvedValue({ items: [doc], page: 1, pageSize: 20, total: 1 });
  vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValue(snapshot());
  vi.mocked(acl.setDocumentPermissions).mockImplementation(async body => body.permissions.map((value, index) => ({ ...entry, ...value, id: index === 0 ? shareId : otherShareId })));
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });
async function setup(granted = permissions, keepAlive = false, tenantId: string | null = null) {
  const context = createShareSession(granted); context.session.currentUser!.tenantId = tenantId; visible = ref(true);
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(View) : h(blank) }) });
  wrapper = mount(keepAlive ? root : View, { global: { plugins: [context.pinia], directives: { loading: {} }, stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } } }); await flushPromises(); return context.session;
}
function vm() { return wrapper!.findComponent(View).vm as unknown as {
  saving: boolean; permissionVersion: number; permissionsReady: boolean; permissionDialogOpen: boolean;
  userId: string; permissionLevel: string; items: typeof doc[]; permissions: typeof entry[];
  openPermissionDialog: (row: typeof doc) => Promise<void>; closePermissionDialog: () => void;
  removePermission: (row: typeof entry) => Promise<void>; finishRemoval: (confirmed: boolean) => void; savePermission: () => Promise<void>;
}; }
async function open() { await vm().openPermissionDialog(doc); await flushPromises(); }
async function remove(row = entry, confirmed = true) { const action = vm().removePermission(row); vm().finishRemoval(confirmed); await action; await flushPromises(); }

describe('文档权限配置移除及快照归属', () => {
  it('移除单个用户保留其余配置，并使用权威快照修订', async () => {
    await setup(); await open(); await remove();
    expect(acl.setDocumentPermissions).toHaveBeenCalledExactlyOnceWith({ documentId: doc.id, version: 7,
      permissions: [{ userId: other.userId, permissionLevel: other.permissionLevel }] }, expect.any(AbortSignal));
    expect(vm().permissions.map(value => value.userId)).toEqual([other.userId]); expect(vm().permissionVersion).toBe(8);
  });
  it('最后一项可通过已有整份替换提交为空，后续新增使用新修订', async () => {
    vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([entry])); await setup(); await open(); await remove();
    expect(vi.mocked(acl.setDocumentPermissions).mock.calls[0]![0]).toEqual({ documentId: doc.id, version: 7, permissions: [] });
    vm().userId = other.userId; vm().permissionLevel = other.permissionLevel; await vm().savePermission();
    expect(vi.mocked(acl.setDocumentPermissions).mock.calls[1]![0]).toEqual({ documentId: doc.id, version: 8, permissions: [{ userId: other.userId, permissionLevel: other.permissionLevel }] });
  });
  it('只读用户不显示移除入口，直接调用也不能写入', async () => {
    await setup([read, aclRead]); await open(); await remove(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
    expect(wrapper!.find('[data-testid="document-permissions-remove"]').exists()).toBe(false);
  });
  it.each([read, aclRead] as const)('缺少父读取 %s 不读取或显示配置', async missing => {
    await setup(permissions.filter(code => code !== missing)); await vm().openPermissionDialog(doc); await remove();
    expect(documents.listDocumentItems).not.toHaveBeenCalled(); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
  });
  it('Host 文本作用域带非空租户不能读取权限', async () => {
    await setup(permissions, false, shareId); await open(); expect(documents.listDocumentItems).not.toHaveBeenCalled(); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled();
  });
  it('放弃确认不写入，也不破坏原快照或新增输入', async () => {
    await setup(); await open(); vm().userId = other.userId; await remove(entry, false);
    expect(acl.setDocumentPermissions).not.toHaveBeenCalled(); expect(vm().permissions).toHaveLength(2); expect(vm().permissionsReady).toBe(true); expect(vm().saving).toBe(false);
    expect(vm().userId).toBe(other.userId);
  });
  it('确认前锁定整份替换，重复移除或新增确认不能并发写入', async () => {
    await setup(); await open(); const action = vm().removePermission(entry); await vm().removePermission(other);
    vm().userId = other.userId; await vm().savePermission(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
    vm().finishRemoval(true); await action; expect(acl.setDocumentPermissions).toHaveBeenCalledOnce();
  });
  it.each(['close', 'revoke', 'unmount'] as const)('等待确认时 %s 移除提示且不写入', async outcome => {
    const session = await setup(); await open(); const action = vm().removePermission(entry); await flushPromises();
    expect(wrapper!.find('[data-testid="document-permissions-remove-confirm"]').exists()).toBe(true);
    if (outcome === 'close') vm().closePermissionDialog(); else if (outcome === 'revoke') session.currentUser!.permissions = [read, aclRead];
    else { wrapper!.unmount(); wrapper = undefined; }
    await action; await flushPromises(); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
    expect(wrapper?.find('[data-testid="document-permissions-remove-confirm"]').exists() ?? false).toBe(false);
  });
  it.each(['resolve', 'reject'] as const)('关闭在途移除，迟到%s不反馈、不改新文档', async outcome => {
    const pending = deferred<typeof entry[]>(); vi.mocked(acl.setDocumentPermissions).mockReturnValueOnce(pending.promise);
    const success = vi.spyOn(feedback, 'showSuccess'); const error = vi.spyOn(feedback, 'showProblem'); await setup(); await open();
    const action = vm().removePermission(entry); vm().finishRemoval(true); await flushPromises();
    const signal = vi.mocked(acl.setDocumentPermissions).mock.calls[0]![1]!; vm().closePermissionDialog(); await open();
    expect(signal.aborted).toBe(true); if (outcome === 'resolve') pending.resolve([other]); else pending.reject(new Error('late'));
    await action; expect(success).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled(); expect(vm().permissions).toHaveLength(2); expect(vm().permissionVersion).toBe(7);
  });
  it('冲突或响应丢失后必须重读，不能用旧快照再次整份替换', async () => {
    vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce({ status: 409, title: '配置已更新', code: 'document.host_permission.version_conflict' });
    await setup(); await open(); await remove(); expect(vm().permissionsReady).toBe(false); expect(vm().permissions).toHaveLength(0);
    await remove(other); vm().userId = shareId; await vm().savePermission(); expect(acl.setDocumentPermissions).toHaveBeenCalledOnce();
    vi.mocked(acl.getDocumentPermissionSnapshot).mockResolvedValueOnce(snapshot([other], 10)); await open(); await remove(other);
    expect(vi.mocked(acl.setDocumentPermissions).mock.calls[1]![0]).toEqual({ documentId: doc.id, version: 10, permissions: [] });
  });
  it('不在已读取配置中的伪造项不能移除', async () => {
    await setup(); await open(); await remove({ ...entry, id: '01980000-0000-7000-8000-000000000099' }); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
  });
  it('确认目标使用 UUID 大小写等价匹配', async () => {
    await setup(); await open(); await remove({ ...entry, id: entry.id.toUpperCase(), userId: entry.userId.toUpperCase() });
    expect(vi.mocked(acl.setDocumentPermissions).mock.calls[0]![0].permissions).toEqual([{ userId: other.userId, permissionLevel: other.permissionLevel }]);
  });
  it('确认前冻结用户和其余配置，原对象变动不能改写请求目标', async () => {
    await setup(); await open(); const target = { ...entry }; const action = vm().removePermission(target); target.userId = other.userId;
    vm().finishRemoval(true); await action; expect(vi.mocked(acl.setDocumentPermissions).mock.calls[0]![0].permissions).toEqual([{ userId: other.userId, permissionLevel: other.permissionLevel }]);
  });
  it('列表修订变化关闭旧权限弹窗并结束移除等待', async () => {
    await setup(); await open(); const action = vm().removePermission(entry); vm().items = [{ ...doc, version: doc.version + 1 }];
    await action; expect(vm().permissionDialogOpen).toBe(false); expect(acl.setDocumentPermissions).not.toHaveBeenCalled();
  });
  it('未知文档不能直接读取配置', async () => {
    await setup(); await vm().openPermissionDialog({ ...doc, id: '01980000-0000-7000-8000-000000000099' }); expect(acl.getDocumentPermissionSnapshot).not.toHaveBeenCalled();
  });
  it('KeepAlive 停用结束确认，恢复只重读目录，不重放移除', async () => {
    await setup(permissions, true); await open(); const action = vm().removePermission(entry); visible.value = false; await flushPromises(); await action;
    visible.value = true; await flushPromises(); expect(documents.listDocumentItems).toHaveBeenCalledTimes(2); expect(acl.getDocumentPermissionSnapshot).toHaveBeenCalledOnce();
    expect(acl.setDocumentPermissions).not.toHaveBeenCalled(); expect(vm().permissionDialogOpen).toBe(false);
  });
});
