import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { useSessionStore } from '../auth/session';
import TenantLeaveDialog from './TenantLeaveDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/tenant-members';

vi.mock('../api/tenant-members', () => ({ getCurrentTenantMember: vi.fn(), leaveCurrentTenantMember: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000010', other = '01981a3f-00c0-7000-8000-000000000020';
const row = { id, tenantId: id, userId: id, username: 'member', displayName: '成员', memberRole: 'Member', status: 'Active',
  createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z', version: 4 };
let wrapper: ReturnType<typeof mount>;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function setup() {
  const pinia = createPinia(); setActivePinia(pinia);
  const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id, username: 'member', displayName: '成员', actorScope: 'host',
    scope: `tenant:${id.replaceAll('-', '')}`, tenantId: id, isSuperAdministrator: false,
    permissions: ['identity.tenant_members.leave_self'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }, currentContextName: 'Acme' });
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/tenant-context', component: { template: '<div />' } }, { path: '/other', component: { template: '<div />' } }
  ] }); await router.push('/tenant-context');
  wrapper = mount(TenantLeaveDialog, { props: { open: true }, global: { plugins: [pinia, router] }, attachTo: document.body });
  await flushPromises(); return { session, router };
}
const dialog = () => wrapper.findComponent(ArtFormDialog);
async function confirm() { dialog().vm.$emit('confirm'); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getCurrentTenantMember).mockResolvedValue({ ...row });
  vi.mocked(api.leaveCurrentTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5 }); });
afterEach(() => { wrapper?.unmount(); });

it('先读取本人会员并明确确认，固定读取版本且双击只写一次', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.leaveCurrentTenantMember).mockReturnValue(pending.promise);
  await setup(); expect(api.leaveCurrentTenantMember).not.toHaveBeenCalled(); expect(dialog().props('showConfirm')).toBe(true);
  await confirm(); await confirm(); expect(api.leaveCurrentTenantMember).toHaveBeenCalledExactlyOnceWith(4, expect.any(AbortSignal));
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises();
  expect(wrapper.emitted('left')).toHaveLength(1); expect(dialog().props('showConfirm')).toBe(false);
});
it('读取等待可关闭，取消信号与晚结果不能恢复确认', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.getCurrentTenantMember).mockReturnValue(pending.promise);
  await setup(); const signal = vi.mocked(api.getCurrentTenantMember).mock.calls[0]![0]!;
  expect(dialog().props('showConfirm')).toBe(false); dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false });
  expect(signal.aborted).toBe(true); pending.resolve(row); await flushPromises(); await confirm();
  expect(api.leaveCurrentTenantMember).not.toHaveBeenCalled(); expect(wrapper.emitted('left')).toBeUndefined();
});
it('关闭重开后旧确认与关闭不能控制新窗口', async () => {
  await setup(); const old = dialog().vm.$.vnode.props!;
  await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  old.onConfirm(); old['onUpdate:open'](false); await flushPromises();
  expect(api.leaveCurrentTenantMember).not.toHaveBeenCalled(); expect(wrapper.emitted('update:open')).toBeUndefined();
  await confirm(); expect(wrapper.emitted('left')).toHaveLength(1);
});
it.each(['tenant', 'user', 'session', 'permission', 'anonymous', 'route'])('等待退出时变更 %s 取消原请求，晚结果无回调', async change => {
  const pending = deferred<typeof row>(); vi.mocked(api.leaveCurrentTenantMember).mockReturnValue(pending.promise);
  const { session, router } = await setup(); await confirm();
  const signal = vi.mocked(api.leaveCurrentTenantMember).mock.calls[0]![1]!;
  if (change === 'tenant') session.currentUser!.tenantId = other;
  if (change === 'user') session.currentUser!.id = other;
  if (change === 'session') session.currentUser!.sessionId = other;
  if (change === 'permission') session.currentUser!.permissions = [];
  if (change === 'anonymous') session.state = 'anonymous';
  if (change === 'route') await router.push('/other');
  await flushPromises(); expect(signal.aborted).toBe(true);
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises(); expect(wrapper.emitted('left')).toBeUndefined();
});
it.each([{ tenantId: other }, { userId: other }, { status: 'Suspended' }, { version: 0 }])('错误成员详情 %j 不开放退出', async patch => {
  vi.mocked(api.getCurrentTenantMember).mockResolvedValue({ ...row, ...patch }); await setup(); await confirm();
  expect(dialog().props('showConfirm')).toBe(false); expect(api.leaveCurrentTenantMember).not.toHaveBeenCalled();
});
it('所有者显示保护原因，不开放退出确认', async () => {
  vi.mocked(api.getCurrentTenantMember).mockResolvedValue({ ...row, memberRole: 'Owner' }); await setup(); await confirm();
  expect(document.body.textContent).toContain('所有者'); expect(api.leaveCurrentTenantMember).not.toHaveBeenCalled();
});
it('并发冲突不自动重发，人工重读后只使用新版本', async () => {
  vi.mocked(api.leaveCurrentTenantMember).mockRejectedValueOnce({ status: 409, code: 'identity.tenant_member_version_conflict', title: '版本已变化' });
  await setup(); await confirm(); expect(dialog().props('showConfirm')).toBe(false); expect(api.getCurrentTenantMember).toHaveBeenCalledTimes(1);
  vi.mocked(api.getCurrentTenantMember).mockResolvedValue({ ...row, version: 6 });
  (document.querySelector('[data-testid="tenant-leave-refresh"]') as HTMLButtonElement).click(); await flushPromises();
  vi.mocked(api.leaveCurrentTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 7 }); await confirm();
  expect(api.leaveCurrentTenantMember).toHaveBeenLastCalledWith(6, expect.any(AbortSignal)); expect(wrapper.emitted('left')).toHaveLength(1);
});
it('在途关闭保留未知结果，重开不自动读取或重复退出', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.leaveCurrentTenantMember).mockReturnValue(pending.promise);
  await setup(); await confirm(); const signal = vi.mocked(api.leaveCurrentTenantMember).mock.calls[0]![1]!;
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  expect(signal.aborted).toBe(true); expect(document.body.textContent).toContain('退出结果尚未确认');
  expect(api.getCurrentTenantMember).toHaveBeenCalledTimes(1); await confirm(); expect(api.leaveCurrentTenantMember).toHaveBeenCalledTimes(1);
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises(); expect(wrapper.emitted('left')).toBeUndefined();
});
it('人工核对已退出的权威状态后接续返回平台，不重发未知写入', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.leaveCurrentTenantMember).mockReturnValue(pending.promise);
  await setup(); await confirm(); dialog().vm.$emit('update:open', false);
  await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  vi.mocked(api.getCurrentTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5 });
  (document.querySelector('[data-testid="tenant-leave-refresh"]') as HTMLButtonElement).click(); await flushPromises();
  expect(wrapper.emitted('left')).toHaveLength(1); expect(api.leaveCurrentTenantMember).toHaveBeenCalledTimes(1);
  pending.resolve({ ...row, status: 'Removed', version: 5 }); await flushPromises(); expect(wrapper.emitted('left')).toHaveLength(1);
});
it.each([{ id: other }, { userId: other }, { tenantId: other }, { status: 'Active' }, { version: 6 }])('错误退出回执 %j 保持未知，不切换上下文', async patch => {
  vi.mocked(api.leaveCurrentTenantMember).mockResolvedValue({ ...row, status: 'Removed', version: 5, ...patch }); await setup(); await confirm();
  expect(document.body.textContent).toContain('退出结果尚未确认'); expect(wrapper.emitted('left')).toBeUndefined();
});
