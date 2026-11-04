import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { useSessionStore } from '../auth/session';
import TenantMemberRemovalDialog from './TenantMemberRemovalDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/tenant-members';

vi.mock('../api/tenant-members', () => ({ getTenantMember: vi.fn(), removeTenantMember: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000010', other = '01981a3f-00c0-7000-8000-000000000020';
const row = { id, tenantId: id, userId: other, username: 'member', displayName: '待移除成员', memberRole: 'Member', status: 'Active',
  createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z', version: 4 };
let wrapper: ReturnType<typeof mount>;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id, username: 'admin', displayName: '管理员', actorScope: 'host',
    scope: `tenant:${id.replaceAll('-', '')}`, tenantId: id, isSuperAdministrator: false,
    permissions: ['identity.tenant_members.read', 'identity.tenant_members.remove'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 } });
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/tenant-members', component: { template: '<div />' } }, { path: '/other', component: { template: '<div />' } }
  ] }); await router.push('/tenant-members');
  wrapper = mount(TenantMemberRemovalDialog, { props: { open: true, target: { ...row } }, global: { plugins: [pinia, router] }, attachTo: document.body });
  await flushPromises(); return { session, router };
}
const dialog = () => wrapper.findComponent(ArtFormDialog);
async function confirm() { dialog().vm.$emit('confirm'); await flushPromises(); }
async function refresh() { (document.querySelector('[data-testid="tenant-member-removal-refresh"]') as HTMLButtonElement).click(); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getTenantMember).mockResolvedValue({ ...row });
  vi.mocked(api.removeTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5 }); });
afterEach(() => { wrapper?.unmount(); });

it('先读取指定成员的当前状态，固定新版本且双击只写一次', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, version: 6 });
  vi.mocked(api.removeTenantMember).mockReturnValue(pending.promise); await setup();
  expect(api.getTenantMember).toHaveBeenCalledExactlyOnceWith(id, expect.any(AbortSignal));
  expect(api.removeTenantMember).not.toHaveBeenCalled(); await confirm(); await confirm();
  expect(api.removeTenantMember).toHaveBeenCalledExactlyOnceWith(id, 6, expect.any(AbortSignal));
  pending.resolve({ ...row, status: 'Removed', version: 7 }); await flushPromises();
  expect(wrapper.emitted('removed')).toEqual([[true]]); expect(dialog().props('showConfirm')).toBe(false);
});
it('读取等待关闭会实际取消，晚结果不能恢复窗口', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.getTenantMember).mockReturnValue(pending.promise); await setup();
  const signal = vi.mocked(api.getTenantMember).mock.calls[0]![1]!;
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true);
  pending.resolve(row); await flushPromises(); await confirm(); expect(api.removeTenantMember).not.toHaveBeenCalled();
});
it('重开后旧确认和旧关闭回调失效', async () => {
  await setup(); const old = dialog().vm.$.vnode.props!;
  await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  old.onConfirm(); old['onUpdate:open'](false); await flushPromises();
  expect(api.removeTenantMember).not.toHaveBeenCalled(); expect(wrapper.emitted('update:open')).toBeUndefined();
  await confirm(); expect(wrapper.emitted('removed')).toEqual([[true]]);
});
it.each(['tenant', 'user', 'session', 'permission', 'readPermission', 'anonymous', 'route'])('在途移除遇到 %s 变化取消等待，晚结果不能回调', async change => {
  const pending = deferred<typeof row>(); vi.mocked(api.removeTenantMember).mockReturnValue(pending.promise);
  const { session, router } = await setup(); await confirm(); const signal = vi.mocked(api.removeTenantMember).mock.calls[0]![2]!;
  if (change === 'tenant') session.currentUser!.tenantId = other;
  if (change === 'user') session.currentUser!.id = other;
  if (change === 'session') session.currentUser!.sessionId = other;
  if (change === 'permission') session.currentUser!.permissions = ['identity.tenant_members.read'];
  if (change === 'readPermission') session.currentUser!.permissions = ['identity.tenant_members.remove'];
  if (change === 'anonymous') session.state = 'anonymous';
  if (change === 'route') await router.push('/other');
  await flushPromises(); expect(signal.aborted).toBe(true); pending.resolve({ ...row, status: 'Removed', version: 5 });
  await flushPromises(); expect(wrapper.emitted('removed')).toBeUndefined();
});
it.each([{ id: other }, { userId: id }, { tenantId: other }, { version: 0 }, { version: 3 }, { memberRole: 'Unknown' }, { status: 'Unknown' }])('拒绝错误详情或低于目录的版本：%j', async patch => {
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, ...patch }); await setup(); await confirm();
  expect(dialog().props('showConfirm')).toBe(false); expect(api.removeTenantMember).not.toHaveBeenCalled();
});
it('错误企业目标不发读取或移除请求', async () => {
  await setup(); vi.clearAllMocks(); await wrapper.setProps({ target: { ...row, tenantId: other } }); await confirm();
  expect(api.getTenantMember).not.toHaveBeenCalled(); expect(api.removeTenantMember).not.toHaveBeenCalled();
});
it('所有者保护不可移除，已移除状态只报告核对事实', async () => {
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, memberRole: 'Owner' }); await setup(); await confirm();
  expect(document.body.textContent).toContain('所有者受到保护'); expect(api.removeTenantMember).not.toHaveBeenCalled();
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5 }); await refresh();
  expect(wrapper.emitted('removed')).toEqual([[false]]); expect(api.removeTenantMember).not.toHaveBeenCalled();
});
it('并发冲突不自动重发，人工重读后明确确认新版本', async () => {
  vi.mocked(api.removeTenantMember).mockRejectedValueOnce({ status: 409, code: 'identity.tenant_member_version_conflict', title: '版本已变化' });
  await setup(); await confirm(); expect(api.getTenantMember).toHaveBeenCalledTimes(1); expect(dialog().props('showConfirm')).toBe(false);
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, version: 6 }); await refresh();
  vi.mocked(api.removeTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 7 }); await confirm();
  expect(api.removeTenantMember).toHaveBeenLastCalledWith(id, 6, expect.any(AbortSignal)); expect(wrapper.emitted('removed')).toEqual([[true]]);
});
it('关闭重开保持未知，人工核对已移除后无需重发', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.removeTenantMember).mockReturnValue(pending.promise); await setup(); await confirm();
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  expect(api.getTenantMember).toHaveBeenCalledTimes(1); expect(document.body.textContent).toContain('移除结果尚未确认'); await confirm();
  expect(api.removeTenantMember).toHaveBeenCalledTimes(1);
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5 }); await refresh();
  expect(wrapper.emitted('removed')).toEqual([[false]]); pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises();
  expect(wrapper.emitted('removed')).toEqual([[false]]);
});
it('在途切换目标后未知标记仍属于原成员，回来不自动读取或移除', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.removeTenantMember).mockReturnValue(pending.promise); await setup(); await confirm();
  vi.mocked(api.getTenantMember).mockResolvedValue({ ...row, id: other });
  await wrapper.setProps({ target: { ...row, id: other } }); await flushPromises(); expect(dialog().props('showConfirm')).toBe(true);
  await wrapper.setProps({ target: { ...row } }); await flushPromises();
  expect(document.body.textContent).toContain('移除结果尚未确认'); expect(api.getTenantMember).toHaveBeenCalledTimes(2);
  await confirm(); expect(api.removeTenantMember).toHaveBeenCalledTimes(1);
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises(); expect(wrapper.emitted('removed')).toBeUndefined();
});
it('同一账号和会话的UUID大小写变化不能解除未知保护', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.removeTenantMember).mockReturnValue(pending.promise);
  const { session } = await setup(); await confirm(); dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false });
  session.currentUser!.id = id.toUpperCase(); session.currentUser!.sessionId = id.toUpperCase(); await flushPromises();
  await wrapper.setProps({ open: true }); await flushPromises(); expect(document.body.textContent).toContain('移除结果尚未确认');
  expect(api.getTenantMember).toHaveBeenCalledTimes(1); await confirm(); expect(api.removeTenantMember).toHaveBeenCalledTimes(1);
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises(); expect(wrapper.emitted('removed')).toBeUndefined();
});
it.each([{ id: other }, { userId: id }, { tenantId: other }, { status: 'Active' }, { version: 6 }])('错误移除回执 %j 保持未知', async patch => {
  vi.mocked(api.removeTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5, ...patch }); await setup(); await confirm();
  expect(document.body.textContent).toContain('移除结果尚未确认'); expect(wrapper.emitted('removed')).toBeUndefined();
});
