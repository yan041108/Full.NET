import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { useSessionStore } from '../auth/session';
import TenantInvitationRevocationDialog from './TenantInvitationRevocationDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as api from '../api/tenant-members';

vi.mock('../api/tenant-members', () => ({ getTenantInvitation: vi.fn(), revokeTenantInvitation: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000010', other = '01981a3f-00c0-7000-8000-000000000020';
const row = { id, tenantId: id, targetEmail: 'invitee@example.test', targetUserId: null, invitedByUserId: id, memberRole: 'Member', status: 'Pending',
  createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z', expiresAtUtc: '2030-01-01T00:00:00Z', version: 4 };
let wrapper: ReturnType<typeof mount>;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id, username: 'admin', displayName: '管理员', actorScope: 'host',
    scope: `tenant:${id.replaceAll('-', '')}`, tenantId: id, isSuperAdministrator: false,
    permissions: ['identity.tenant_members.read', 'identity.tenant_members.revoke_invitation'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 } });
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/tenant-members', component: { template: '<div />' } }, { path: '/other', component: { template: '<div />' } }
  ] }); await router.push('/tenant-members');
  wrapper = mount(TenantInvitationRevocationDialog, { props: { open: true, target: { ...row } }, global: { plugins: [pinia, router] }, attachTo: document.body });
  await flushPromises(); return { session, router };
}
const dialog = () => wrapper.findComponent(ArtFormDialog);
async function confirm() { dialog().vm.$emit('confirm'); await flushPromises(); }
async function refresh() { (document.querySelector('[data-testid="tenant-invitation-revocation-refresh"]') as HTMLButtonElement).click(); await flushPromises(); }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row });
  vi.mocked(api.revokeTenantInvitation).mockResolvedValue({ ...row, status: 'Revoked', version: 5 }); });
afterEach(() => { wrapper?.unmount(); });

it('先核对指定邀请再明确撤销，重复确认只写一次并核对精确回执', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.revokeTenantInvitation).mockReturnValue(pending.promise); await setup();
  expect(api.getTenantInvitation).toHaveBeenCalledExactlyOnceWith(id, expect.any(AbortSignal)); expect(api.revokeTenantInvitation).not.toHaveBeenCalled();
  expect(document.body.textContent).toContain('受邀身份'); expect(document.body.textContent).toContain('有效期至');
  await confirm(); await confirm(); expect(api.revokeTenantInvitation).toHaveBeenCalledExactlyOnceWith(id, expect.any(AbortSignal));
  pending.resolve({ ...row, status: 'Revoked', version: 5 }); await flushPromises(); expect(wrapper.emitted('revoked')).toEqual([[true]]);
});
it('详情等待关闭实际取消，晚结果不恢复确认', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.getTenantInvitation).mockReturnValue(pending.promise); await setup();
  const signal = vi.mocked(api.getTenantInvitation).mock.calls[0]![1]!;
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); expect(signal.aborted).toBe(true);
  pending.resolve(row); await flushPromises(); await confirm(); expect(api.revokeTenantInvitation).not.toHaveBeenCalled();
});
it('重开后旧确认和关闭不能控制新窗口', async () => {
  await setup(); const old = dialog().vm.$.vnode.props!;
  await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  old.onConfirm(); old['onUpdate:open'](false); await flushPromises(); expect(api.revokeTenantInvitation).not.toHaveBeenCalled();
  expect(wrapper.emitted('update:open')).toBeUndefined(); await confirm(); expect(wrapper.emitted('revoked')).toEqual([[true]]);
});
it.each(['tenant', 'user', 'session', 'permission', 'readPermission', 'anonymous', 'route'])('在途撤销遇到 %s 变化取消等待，晚回执失效', async change => {
  const pending = deferred<typeof row>(); vi.mocked(api.revokeTenantInvitation).mockReturnValue(pending.promise);
  const { session, router } = await setup(); await confirm(); const signal = vi.mocked(api.revokeTenantInvitation).mock.calls[0]![1]!;
  if (change === 'tenant') session.currentUser!.tenantId = other;
  if (change === 'user') session.currentUser!.id = other;
  if (change === 'session') session.currentUser!.sessionId = other;
  if (change === 'permission') session.currentUser!.permissions = ['identity.tenant_members.read'];
  if (change === 'readPermission') session.currentUser!.permissions = ['identity.tenant_members.revoke_invitation'];
  if (change === 'anonymous') session.state = 'anonymous';
  if (change === 'route') await router.push('/other');
  await flushPromises(); expect(signal.aborted).toBe(true); pending.resolve({ ...row, status: 'Revoked', version: 5 });
  await flushPromises(); expect(wrapper.emitted('revoked')).toBeUndefined();
});
it.each([{ id: other }, { tenantId: other }, { invitedByUserId: other }, { targetEmail: 'other@example.test' },
  { version: 0 }, { version: 3 }, { status: 'Unknown' }])('错误详情或陈旧版本不能撤销：%j', async patch => {
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, ...patch }); await setup(); await confirm();
  expect(dialog().props('showConfirm')).toBe(false); expect(api.revokeTenantInvitation).not.toHaveBeenCalled();
});
it.each(['Accepted', 'Expired'])('终态 %s 不提供撤销，也不假报写入成功', async status => {
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, status, version: 5 }); await setup(); await confirm();
  expect(dialog().props('showConfirm')).toBe(false); expect(api.revokeTenantInvitation).not.toHaveBeenCalled(); expect(wrapper.emitted('revoked')).toBeUndefined();
});
it('人工核对已撤销只报告当前事实，不发写请求', async () => {
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, status: 'Revoked', version: 5 }); await setup(); await confirm();
  expect(wrapper.emitted('revoked')).toEqual([[false]]); expect(api.revokeTenantInvitation).not.toHaveBeenCalled();
});
it('冲突后不自动重读重发，核对已接受后不可撤销', async () => {
  vi.mocked(api.revokeTenantInvitation).mockRejectedValueOnce({ status: 409, code: 'identity.tenant_invitations.invalid', title: '邀请已变化' });
  await setup(); await confirm(); expect(api.getTenantInvitation).toHaveBeenCalledTimes(1); expect(dialog().props('showConfirm')).toBe(false);
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, status: 'Accepted', version: 5 }); await refresh(); await confirm();
  expect(document.body.textContent).toContain('不能通过撤销移除成员'); expect(api.revokeTenantInvitation).toHaveBeenCalledTimes(1);
});
it('在途关闭重开保持未知，人工核对Revoked后不重发', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.revokeTenantInvitation).mockReturnValue(pending.promise); await setup(); await confirm();
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
  expect(document.body.textContent).toContain('撤销结果尚未确认'); expect(api.getTenantInvitation).toHaveBeenCalledTimes(1); await confirm();
  expect(api.revokeTenantInvitation).toHaveBeenCalledTimes(1);
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, status: 'Revoked', version: 5 }); await refresh();
  expect(wrapper.emitted('revoked')).toEqual([[false]]); pending.resolve({ ...row, status: 'Revoked', version: 5 }); await flushPromises();
  expect(wrapper.emitted('revoked')).toEqual([[false]]);
});
it('切换邀请再回来不能解除原请求的未知保护', async () => {
  const pending = deferred<typeof row>(); vi.mocked(api.revokeTenantInvitation).mockReturnValue(pending.promise); await setup(); await confirm();
  vi.mocked(api.getTenantInvitation).mockResolvedValue({ ...row, id: other }); await wrapper.setProps({ target: { ...row, id: other } }); await flushPromises();
  await wrapper.setProps({ target: { ...row } }); await flushPromises(); expect(document.body.textContent).toContain('撤销结果尚未确认');
  expect(api.getTenantInvitation).toHaveBeenCalledTimes(2); await confirm(); expect(api.revokeTenantInvitation).toHaveBeenCalledTimes(1);
});
it.each([{ id: other }, { tenantId: other }, { invitedByUserId: other }, { targetEmail: 'other@example.test' }, { status: 'Accepted' }, { version: 6 }])('错误回执 %j 保持未知而不回调', async patch => {
  vi.mocked(api.revokeTenantInvitation).mockResolvedValue({ ...row, status: 'Revoked', version: 5, ...patch }); await setup(); await confirm();
  expect(document.body.textContent).toContain('撤销结果尚未确认'); expect(wrapper.emitted('revoked')).toBeUndefined();
});
