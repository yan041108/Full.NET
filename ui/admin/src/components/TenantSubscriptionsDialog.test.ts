import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import TenantSubscriptionsDialog from './TenantSubscriptionsDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { cancelTenantSubscription, createTenantSubscription, listTenantSubscriptions } from '../api/tenant-subscriptions';

vi.mock('../api/tenant-subscriptions', () => ({ listTenantSubscriptions: vi.fn(), createTenantSubscription: vi.fn(), cancelTenantSubscription: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const tenant = { id: id(1), identifier: 'acme', name: 'Acme', domain: 'acme.localhost', isActive: true, version: 2, defaultLocale: 'zh-CN' };
const pkg = { id: id(2), code: 'one', name: 'First plan', description: null, isActive: true, version: 1, assignedTenantCount: 0 };
const second = { ...pkg, id: id(3), name: 'Chosen plan' };
const row = { id: id(5), tenantId: tenant.id, packageId: pkg.id, status: 'Trial', currentPeriodStartUtc: '2026-11-01T00:00:00Z', currentPeriodEndUtc: '2026-12-01T00:00:00Z', version: 4 };
const page = (items = [row]) => ({ items, page: 1, pageSize: 20, total: items.length });
const dialog = { props: ['open', 'showConfirm', 'confirmDisabled'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm !== false" data-testid="confirm" :disabled="confirmDisabled" @click="$emit(\'confirm\')">Confirm</button><button data-testid="close" @click="$emit(\'update:open\', false)">Close</button></section>' };
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
async function panel(options: { readOnly?: boolean; active?: boolean } = {}) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'operator', displayName: 'Operator', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, sessionId: id(10), permissions: ['tenancy.tenant_subscriptions.read', ...(options.readOnly ? [] : ['tenancy.tenant_subscriptions.manage'])], preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(TenantSubscriptionsDialog, { props: { open: true, tenant: { ...tenant, isActive: options.active ?? true }, packages: [pkg, second] },
    global: { plugins: [pinia], stubs: { ArtFormDialog: dialog, PaidSubscriptionPurchase: true, SubscriptionCheckoutAction: true } } });
  await flushPromises(); return { wrapper, session };
}
describe('租户订阅操作台', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(listTenantSubscriptions).mockResolvedValue(page()); });
  it('明确选择套餐并预览三十天试用，确认前不写入', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([]));
    vi.mocked(createTenantSubscription).mockImplementation(async (tenantId, body) => ({ ...row, ...body, id: id(6), tenantId, version: 1 }));
    const { wrapper } = await panel(); expect(wrapper.get('[data-testid="tenant-subscription-trial"]').attributes('disabled')).toBeDefined();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', second.id); await flushPromises();
    await wrapper.get('[data-testid="tenant-subscription-trial"]').trigger('click');
    expect(wrapper.text()).toContain('Chosen plan'); expect(createTenantSubscription).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [target, body, signal] = vi.mocked(createTenantSubscription).mock.calls[0]!;
    expect(target).toBe(tenant.id); expect(body.packageId).toBe(second.id); expect(body.status).toBe('Trial'); expect(signal).toBeInstanceOf(AbortSignal);
    expect(Date.parse(body.currentPeriodEndUtc) - Date.parse(body.currentPeriodStartUtc)).toBe(30 * 86400000);
    expect(wrapper.emitted('changed')).toHaveLength(1); wrapper.unmount();
  });
  it('取消先确认原订阅和版本，再提交一次', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    const { wrapper } = await panel(); await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click');
    expect(cancelTenantSubscription).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('不会自动发起渠道退款');
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(cancelTenantSubscription).toHaveBeenCalledWith(tenant.id, row.id, 4, expect.any(AbortSignal)); expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('只读用户能读取历史但没有试用和取消入口', async () => {
    const { wrapper } = await panel({ readOnly: true }); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('试用中'); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="tenant-subscription-trial"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['Expired', 'Cancelled'])('终态 %s 不提供取消', async status => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status }])); const { wrapper } = await panel();
    expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false); wrapper.unmount();
  });
  it('停用租户仍能核对历史及取消，不能新增试用', async () => {
    const { wrapper } = await panel({ active: false }); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tenant-subscription-trial"]').exists()).toBe(false); wrapper.unmount();
  });
  it('切换租户后迟到旧列表不能覆盖新列表', async () => {
    const pending = deferred<ReturnType<typeof page>>(); vi.mocked(listTenantSubscriptions).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(page([]));
    const { wrapper } = await panel(); const signal = vi.mocked(listTenantSubscriptions).mock.calls[0]![3];
    await wrapper.setProps({ tenant: { ...tenant, id: id(8), name: 'Other tenant' } }); await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve(page()); await flushPromises();
    expect(wrapper.text()).toContain('Other tenant'); expect(wrapper.text()).not.toContain('试用中'); wrapper.unmount();
  });
  it('错配租户列表失败关闭写入资格', async () => {
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, tenantId: id(8) }])); const { wrapper } = await panel();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.find('[data-testid="tenant-subscription-cancel"]').exists()).toBe(false); wrapper.unmount();
  });
  it('取消结果未知先刷新，不自动重发旧版本', async () => {
    vi.mocked(cancelTenantSubscription).mockRejectedValue(new Error('timeout')); const { wrapper } = await panel();
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="tenant-subscription-cancel"]').attributes('disabled')).toBeDefined();
    expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); expect(listTenantSubscriptions).toHaveBeenCalledTimes(1);
    vi.mocked(listTenantSubscriptions).mockResolvedValue(page([{ ...row, status: 'Cancelled', version: 5 }]));
    await wrapper.get('[data-testid="tenant-subscriptions-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('已取消'); expect(cancelTenantSubscription).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('取消已成功但随后重读失败仍保留成功回执', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, status: 'Cancelled', version: 5 });
    const { wrapper } = await panel(); vi.mocked(listTenantSubscriptions).mockRejectedValue(new Error('read failed'));
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-operation-receipt"]').text()).toContain('操作已确认完成'); expect(wrapper.text()).toContain('订阅历史读取失败');
    expect(wrapper.emitted('changed')).toHaveLength(1); wrapper.unmount();
  });
  it('取消响应错配目标时不显示成功或重新启用写入', async () => {
    vi.mocked(cancelTenantSubscription).mockResolvedValue({ ...row, id: id(8), status: 'Cancelled' }); const { wrapper } = await panel();
    await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.emitted('changed')).toBeUndefined();
    expect(wrapper.get('[data-testid="tenant-subscription-cancel"]').attributes('disabled')).toBeDefined(); wrapper.unmount();
  });
  it('撤权会取消办理并丢弃迟到成功', async () => {
    const pending = deferred<typeof row>(); vi.mocked(cancelTenantSubscription).mockReturnValue(pending.promise);
    const { wrapper, session } = await panel(); await wrapper.get('[data-testid="tenant-subscription-cancel"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(cancelTenantSubscription).mock.calls[0]![3]; session.currentUser!.permissions = []; await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve({ ...row, status: 'Cancelled' }); await flushPromises(); expect(wrapper.emitted('changed')).toBeUndefined(); wrapper.unmount();
  });
});
