import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { ElInput } from 'element-plus';
import { useSessionStore } from '../auth/session';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import TenantOwnerTransferDialog from './TenantOwnerTransferDialog.vue';
import { retainOwnerSession } from './tenant-owner-pending';
import * as members from '../api/tenant-members';
import * as api from '../api/tenant-ownership';
import type { TenantOwnerTransferRequest, TenantOwnerTransferReceipt } from '@fullnet/client-contracts';

vi.mock('../api/tenant-members', () => ({ getTenantMember: vi.fn() }));
vi.mock('../api/tenant-ownership', () => ({ getTenantOwnership: vi.fn(), transferTenantOwner: vi.fn(), getTenantOwnerTransferReceipt: vi.fn() }));
const id = (n: number) => `01981a3f-00c0-7000-8000-${String(n).padStart(12, '0')}`;
const actor = id(1), binding = id(2), tenant = id(3), target = id(4), receiver = id(5);
const row = { id: target, tenantId: tenant, userId: receiver, username: 'receiver', displayName: '接收成员', memberRole: 'Member', status: 'Active',
  createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z', version: 4 };
const owner = { ...row, id: id(6), userId: actor, username: 'owner', displayName: '当前所有者', memberRole: 'Owner', version: 6 };
const state = { tenantId: tenant, owner, canInitialize: false, canTransfer: true, requiresTotp: true };
const receipt = (body: TenantOwnerTransferRequest): TenantOwnerTransferReceipt => ({ tenantId: tenant, operationId: body.operationId,
  actorUserId: actor, sourceMemberId: body.sourceMemberId, sourceUserId: body.sourceMemberId ? actor : null,
  sourcePreviousVersion: body.sourceVersion, sourceVersion: body.sourceVersion == null ? null : body.sourceVersion + 1,
  targetMemberId: body.targetMemberId, targetUserId: receiver, targetPreviousVersion: body.targetVersion, targetVersion: body.targetVersion + 1,
  createdAtUtc: '2026-11-04T00:00:00Z' });
let wrapper: ReturnType<typeof mount>;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore();
  session.$patch({ state: 'authenticated', currentUser: { id: actor, username: 'owner', displayName: '所有者', actorScope: 'host',
    scope: `tenant:${tenant.replaceAll('-', '')}`, tenantId: tenant, sessionId: binding, isSuperAdministrator: false,
    permissions: ['identity.tenant_members.read', 'identity.tenant_members.transfer_ownership'], preferredLocale: 'zh-CN', profileVersion: 1 } });
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/tenant-members', component: { template: '<div />' } }, { path: '/other', component: { template: '<div />' } }
  ] }); await router.push('/tenant-members');
  wrapper = mount(TenantOwnerTransferDialog, { props: { open: true, target: { ...row } }, global: { plugins: [pinia, router] }, attachTo: document.body });
  await flushPromises(); return { session, router };
}
const dialog = () => wrapper.findComponent(ArtFormDialog);
async function fill() { const fields = wrapper.findAllComponents(ElInput);
  fields[0]?.vm.$emit('update:modelValue', 'current-owner-password'); fields[1]?.vm.$emit('update:modelValue', '593712'); await flushPromises(); }
async function confirm() { dialog().vm.$emit('confirm'); await flushPromises(); }
async function click(testId: string) { (document.querySelector(`[data-testid="${testId}"]`) as HTMLButtonElement).click(); await flushPromises(); }
beforeEach(() => {
  vi.clearAllMocks(); retainOwnerSession('reset-user', 'reset-session');
  vi.mocked(members.getTenantMember).mockResolvedValue({ ...row }); vi.mocked(api.getTenantOwnership).mockResolvedValue({ ...state });
  vi.mocked(api.transferTenantOwner).mockImplementation(async body => receipt(body));
});
afterEach(() => { wrapper?.unmount(); });

it('先读取真实 Owner 与接收版本，填写强认证后固定操作且双击只写一次', async () => {
  const pending = deferred<TenantOwnerTransferReceipt>(); vi.mocked(api.transferTenantOwner).mockReturnValue(pending.promise);
  await setup(); expect(api.transferTenantOwner).not.toHaveBeenCalled(); await confirm(); expect(api.transferTenantOwner).not.toHaveBeenCalled();
  await fill(); await confirm(); await confirm(); expect(api.transferTenantOwner).toHaveBeenCalledTimes(1);
  const body = vi.mocked(api.transferTenantOwner).mock.calls[0]![0];
  expect(body).toMatchObject({ sourceMemberId: owner.id, sourceVersion: 6, targetMemberId: target, targetVersion: 4,
    currentPassword: 'current-owner-password', totpCode: '593712' });
  pending.resolve(receipt(body)); await flushPromises(); expect(wrapper.emitted('changed')).toEqual([[true]]);
  expect(document.querySelector('#tenant-owner-current-password')).toBeNull();
});
it('首次建立只依据服务端资格，回执中源成员为空', async () => {
  vi.mocked(api.getTenantOwnership).mockResolvedValue({ ...state, owner: null, canInitialize: true, canTransfer: false, requiresTotp: false });
  await setup(); await fill(); await confirm(); const body = vi.mocked(api.transferTenantOwner).mock.calls[0]![0];
  expect(body.sourceMemberId).toBeUndefined(); expect(body.sourceVersion).toBeUndefined(); expect(wrapper.emitted('changed')).toEqual([[true]]);
});
it.each(['tenant', 'user', 'session', 'permission', 'readPermission', 'anonymous', 'route'])('在途交接遇到 %s 变化会取消等待并隔离晚结果', async change => {
  const pending = deferred<TenantOwnerTransferReceipt>(); vi.mocked(api.transferTenantOwner).mockReturnValue(pending.promise);
  const { session, router } = await setup(); await fill(); await confirm();
  const [body, signal] = vi.mocked(api.transferTenantOwner).mock.calls[0]!;
  if (change === 'tenant') session.currentUser!.tenantId = id(8);
  if (change === 'user') session.currentUser!.id = id(8);
  if (change === 'session') session.currentUser!.sessionId = id(8);
  if (change === 'permission') session.currentUser!.permissions = ['identity.tenant_members.read'];
  if (change === 'readPermission') session.currentUser!.permissions = ['identity.tenant_members.transfer_ownership'];
  if (change === 'anonymous') session.state = 'anonymous'; if (change === 'route') await router.push('/other');
  await flushPromises(); expect(signal!.aborted).toBe(true); pending.resolve(receipt(body)); await flushPromises();
  expect(wrapper.emitted('changed')).toBeUndefined(); expect(document.querySelector('#tenant-owner-current-password')).toBeNull();
});
it.each([{ tenantId: id(9) }, { userId: actor }, { version: 0 }, { version: 3 }])('拒绝错误接收详情：%j', async patch => {
  vi.mocked(members.getTenantMember).mockResolvedValue({ ...row, ...patch }); await setup(); await fill(); await confirm();
  expect(api.transferTenantOwner).not.toHaveBeenCalled();
});
it.each([{ memberRole: 'Owner' }, { status: 'Removed' }, { status: 'Pending' }, { status: 'Suspended' }])('接收成员不可交接：%j', async patch => {
  vi.mocked(members.getTenantMember).mockResolvedValue({ ...row, ...patch }); await setup(); await confirm();
  expect(dialog().props('showConfirm')).toBe(false); expect(api.transferTenantOwner).not.toHaveBeenCalled();
});
it('服务端无资格时不提供强认证输入和确认', async () => {
  vi.mocked(api.getTenantOwnership).mockResolvedValue({ ...state, canTransfer: false }); await setup(); await confirm();
  expect(document.querySelector('#tenant-owner-current-password')).toBeNull(); expect(api.transferTenantOwner).not.toHaveBeenCalled();
});
it('关闭在途窗口、销毁再重开保留原操作，人工核对不读取当前详情或重发写入', async () => {
  const pending = deferred<TenantOwnerTransferReceipt>(); vi.mocked(api.transferTenantOwner).mockReturnValue(pending.promise);
  await setup(); await fill(); await confirm(); const [body, signal] = vi.mocked(api.transferTenantOwner).mock.calls[0]!;
  dialog().vm.$emit('update:open', false); await wrapper.setProps({ open: false }); wrapper.unmount(); expect(signal!.aborted).toBe(true);
  vi.mocked(members.getTenantMember).mockClear(); vi.mocked(api.getTenantOwnership).mockClear();
  vi.mocked(api.getTenantOwnerTransferReceipt).mockResolvedValue(receipt(body)); await setup();
  expect(members.getTenantMember).not.toHaveBeenCalled(); expect(api.getTenantOwnership).not.toHaveBeenCalled();
  await confirm(); expect(api.transferTenantOwner).toHaveBeenCalledTimes(1); await click('tenant-owner-transfer-check');
  expect(api.getTenantOwnerTransferReceipt).toHaveBeenCalledExactlyOnceWith(body.operationId, expect.any(AbortSignal));
  expect(wrapper.emitted('changed')).toEqual([[false]]); pending.resolve(receipt(body)); await flushPromises();
  expect(wrapper.emitted('changed')).toEqual([[false]]);
});
it('回执暂不可见仍保留未知，不用当前 Owner 状态猜测并重发', async () => {
  vi.mocked(api.transferTenantOwner).mockRejectedValue(new TypeError('network'));
  vi.mocked(api.getTenantOwnerTransferReceipt).mockRejectedValue({ status: 404, code: 'identity.tenant_ownership.not_found', title: 'Not found' });
  await setup(); await fill(); await confirm(); await click('tenant-owner-transfer-check'); await confirm();
  expect(api.transferTenantOwner).toHaveBeenCalledTimes(1); expect(document.body.textContent).toContain('原请求可能仍在执行');
});
it('旧确认和关闭事件不能控制重开窗口', async () => {
  await setup(); await fill(); const old = dialog().vm.$.vnode.props!;
  await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises(); await fill();
  old.onConfirm(); old['onUpdate:open'](false); await flushPromises();
  expect(api.transferTenantOwner).not.toHaveBeenCalled(); expect(wrapper.emitted('update:open')).toBeUndefined();
});
it.each([{ actorUserId: id(9) }, { tenantId: id(9) }, { operationId: id(9) }, { targetUserId: id(9) }, { sourceVersion: 8 }])('错误回执保持未知：%j', async patch => {
  vi.mocked(api.transferTenantOwner).mockImplementation(async body => ({ ...receipt(body), ...patch }));
  await setup(); await fill(); await confirm(); await confirm(); expect(wrapper.emitted('changed')).toBeUndefined();
  expect(api.transferTenantOwner).toHaveBeenCalledTimes(1); expect(document.body.textContent).toContain('结果未知');
});
it('401 不在组件自动重放，手动刷新前清空密码和源版本', async () => {
  vi.mocked(api.transferTenantOwner).mockRejectedValue({ status: 401, code: 'identity.reauthentication_failed', title: 'Verify again' });
  await setup(); await fill(); await confirm(); expect(api.transferTenantOwner).toHaveBeenCalledTimes(1);
  expect(document.querySelector('#tenant-owner-current-password')).toBeNull(); expect(wrapper.emitted('changed')).toBeUndefined();
});
