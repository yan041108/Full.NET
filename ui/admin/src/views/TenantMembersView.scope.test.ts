import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { useSessionStore } from '../auth/session';
import TenantMembersView from './TenantMembersView.vue';
import * as api from '../api/tenant-members';

vi.mock('../api/tenant-members', () => ({ listTenantMembers: vi.fn(), listTenantInvitations: vi.fn(),
  createTenantInvitation: vi.fn(), listInvitationRegistrationWays: vi.fn(), provisionTenantMember: vi.fn(),
  revokeTenantInvitation: vi.fn(), getTenantInvitation: vi.fn(), updateTenantMember: vi.fn(), getTenantMember: vi.fn(), removeTenantMember: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000010', other = '01981a3f-00c0-7000-8000-000000000020';
const page = { items: [], total: 0, page: 1, pageSize: 20 };
const row = { id, tenantId: id, userId: id, username: 'old-member', displayName: '原企业成员', memberRole: 'Member', status: 'Active',
  createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z', version: 4 };
let wrapper: ReturnType<typeof mount>;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id, username: 'admin', displayName: '管理员', actorScope: 'host',
    scope: `tenant:${id.replaceAll('-', '')}`, tenantId: id, isSuperAdministrator: false,
    permissions: ['identity.tenant_members.read', 'identity.tenant_members.remove'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 } });
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/identity/tenant-members', component: TenantMembersView }] });
  await router.push('/identity/tenant-members');
  wrapper = mount(TenantMembersView, { global: { plugins: [pinia, router] } }); await flushPromises(); return session;
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.listTenantMembers).mockResolvedValue(page);
  vi.mocked(api.listTenantInvitations).mockResolvedValue(page); });
afterEach(() => { wrapper?.unmount(); });

it.each(['tenant', 'user', 'session', 'permission', 'anonymous'])('列表读取过程中 %s 变化取消原请求，迟到结果不进入新页面', async change => {
  const pending = deferred<{ items: typeof row[]; total: number; page: number; pageSize: number }>();
  vi.mocked(api.listTenantMembers).mockReturnValueOnce(pending.promise);
  const session = await setup(); const signal = vi.mocked(api.listTenantMembers).mock.calls[0]![1]!;
  if (change === 'tenant') session.currentUser!.tenantId = other;
  if (change === 'user') session.currentUser!.id = other;
  if (change === 'session') session.currentUser!.sessionId = other;
  if (change === 'permission') session.currentUser!.permissions = [];
  if (change === 'anonymous') session.state = 'anonymous';
  await flushPromises(); expect(signal.aborted).toBe(true);
  pending.resolve({ ...page, items: [row], total: 1 }); await flushPromises();
  expect(wrapper.findAllComponents({ name: 'ElTable' })[0]!.props('data')).toEqual([]);
  expect(wrapper.text()).not.toContain('原企业成员');
});
it('会话变化同时取消邀请列表，原邀请晚返回不填入当前页面', async () => {
  const pending = deferred<Awaited<ReturnType<typeof api.listTenantInvitations>>>(); vi.mocked(api.listTenantInvitations).mockReturnValueOnce(pending.promise);
  const session = await setup(); const signal = vi.mocked(api.listTenantInvitations).mock.calls[0]![1]!;
  session.currentUser!.sessionId = other; await flushPromises(); expect(signal.aborted).toBe(true);
  pending.resolve({ ...page, items: [{ ...row, targetEmail: 'old-private@example.test', invitedByUserId: id, expiresAtUtc: '2030-01-01T00:00:00Z' }], total: 1 }); await flushPromises();
  expect(wrapper.findAllComponents({ name: 'ElTable' })[1]!.props('data')).toEqual([]);
});
it('已展示成员在读取权限撤销后立即清除，不继续发成员或邀请请求', async () => {
  vi.mocked(api.listTenantMembers).mockResolvedValue({ ...page, items: [row], total: 1 }); const session = await setup();
  expect(wrapper.findAllComponents({ name: 'ElTable' })[0]!.props('data')).toEqual([row]);
  session.currentUser!.permissions = []; await flushPromises();
  expect(wrapper.findAllComponents({ name: 'ElTable' })[0]!.props('data')).toEqual([]);
  expect(api.listTenantMembers).toHaveBeenCalledTimes(1); expect(api.listTenantInvitations).toHaveBeenCalledTimes(1);
});
it('所有者交接按钮使用独立操作权限，撤销后立即移除', async () => {
  vi.mocked(api.listTenantMembers).mockResolvedValue({ ...page, items: [row], total: 1 }); const session = await setup();
  expect(wrapper.find('[data-testid="tenant-members-action-transfer-owner"]').exists()).toBe(false);
  session.currentUser!.permissions.push('identity.tenant_members.transfer_ownership'); await flushPromises();
  expect(wrapper.find('[data-testid="tenant-members-action-transfer-owner"]').exists()).toBe(true);
  session.currentUser!.permissions = ['identity.tenant_members.read']; await flushPromises();
  expect(wrapper.find('[data-testid="tenant-members-action-transfer-owner"]').exists()).toBe(false);
});
