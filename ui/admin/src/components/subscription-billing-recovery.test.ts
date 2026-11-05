import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect } from 'element-plus';
import type { SubscriptionCheckoutResponse } from '@fullnet/client-contracts';
import PaidSubscriptionPurchase from './PaidSubscriptionPurchase.vue';
import SubscriptionCheckoutAction from './SubscriptionCheckoutAction.vue';
import SubscriptionFulfillmentAction from './SubscriptionFulfillmentAction.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { createPaidSubscription, getPackageOffer, checkoutSubscription, getSubscriptionFulfillment, retrySubscriptionFulfillment } from '../api/subscription-billing';

vi.mock('../api/subscription-billing', () => ({ createPaidSubscription: vi.fn(), getPackageOffer: vi.fn(), checkoutSubscription: vi.fn(), getSubscriptionFulfillment: vi.fn(), retrySubscriptionFulfillment: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const permissions = ['tenancy.tenant_packages.read', 'tenancy.tenant_subscriptions.manage', 'payments.orders.create', 'payments.orders.read', 'tenancy.tenant_subscriptions.read', 'payments.orders.reconcile'];
const pkg = { id: id(2), code: 'plan', name: 'Plan', description: null, isActive: true, version: 1, assignedTenantCount: 0 };
const offer = { packageId: pkg.id, paymentTenantId: id(3), merchantConfigId: id(4), amountMinor: 12500, currency: 'CNY', seatLimit: 20, storageLimit: 1000, durationDays: 30, entitlementIds: [], isActive: true, version: 3 };
const fulfillment = { subscriptionId: id(5), beneficiaryTenantId: id(1), paymentOrderId: id(6), stateKey: 'Review', errorCode: null, attempt: 1, updatedAtUtc: '2026-11-05T00:00:00Z' };
const checkout = { subscriptionId: id(5), fulfillmentStateKey: 'Awaiting', errorCode: null, order: { id: id(6), tradeStateKey: 'pending', codeUrl: 'weixin://original' } } as SubscriptionCheckoutResponse;
const dialog = { props: ['open', 'showConfirm', 'confirmDisabled'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm !== false" data-testid="confirm" :disabled="confirmDisabled" @click="$emit(\'confirm\')">Confirm</button><button data-testid="close" @click="$emit(\'update:open\', false)">Close</button></section>' };
function sessionSetup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'billing', displayName: 'Billing', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, sessionId: id(10), permissions: [...permissions], preferredLocale: 'zh-CN', profileVersion: 1 };
  return { pinia, session };
}
const globalFor = (pinia: ReturnType<typeof createPinia>) => ({ plugins: [pinia], stubs: { ArtFormDialog: dialog } });
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
async function purchase(source?: { subscriptionId: string; packageId: string }) {
  const state = sessionSetup(); const wrapper = mount(PaidSubscriptionPurchase, { props: { tenantId: id(1), packages: [pkg], source }, global: globalFor(state.pinia) });
  await wrapper.get(`[data-testid="${source ? 'subscription-repurchase' : 'paid-subscription-create'}"]`).trigger('click');
  if (!source) wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', pkg.id); await flushPromises();
  await wrapper.findAll('button').find(button => button.text() === '读取报价')!.trigger('click'); await flushPromises();
  return { wrapper, ...state };
}
describe('付费账单原请求恢复', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(getPackageOffer).mockResolvedValue(offer); vi.mocked(getSubscriptionFulfillment).mockResolvedValue(fulfillment); });
  it('从到期历史明确读取当前报价，再创建独立新周期', async () => {
    vi.mocked(createPaidSubscription).mockImplementation(async (tenant, body) => ({ ...offer, tenantId: tenant, subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' }));
    const { wrapper } = await purchase({ subscriptionId: id(11), packageId: pkg.id });
    expect(createPaidSubscription).not.toHaveBeenCalled(); expect(getPackageOffer).toHaveBeenCalledWith(pkg.id, expect.any(AbortSignal));
    expect(wrapper.findComponent(ElSelect).props('disabled')).toBe(true); expect(wrapper.get('[data-testid="repurchase-source"]').text()).toBe(id(11));
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const body = vi.mocked(createPaidSubscription).mock.calls[0]![1];
    expect(body.packageId).toBe(pkg.id); expect(body.offerVersion).toBe(3);
    expect(Object.keys(body).sort()).toEqual(['effectiveFromUtc', 'idempotencyKey', 'offerVersion', 'packageId']);
    expect(Date.parse(body.effectiveFromUtc)).toBeGreaterThan(Date.parse('2026-01-01T00:00:00Z'));
    expect(wrapper.emitted('created')).toHaveLength(1); wrapper.unmount();
  });
  it('历史套餐已停用时不提供原套餐购买入口', async () => {
    const { pinia } = sessionSetup(); const wrapper = mount(PaidSubscriptionPurchase, { props: { tenantId: id(1), packages: [{ ...pkg, isActive: false }], source: { subscriptionId: id(11), packageId: pkg.id } }, global: globalFor(pinia) });
    expect(wrapper.find('[data-testid="subscription-repurchase"]').exists()).toBe(false); expect(getPackageOffer).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('返回原到期订阅身份不能冒充新周期', async () => {
    vi.mocked(createPaidSubscription).mockImplementation(async (tenant, body) => ({ ...offer, tenantId: tenant, subscriptionId: id(11), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' }));
    const { wrapper } = await purchase({ subscriptionId: id(11), packageId: pkg.id }); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.emitted('created')).toBeUndefined(); wrapper.unmount();
  });
  it('等待购买时切换历史来源会取消并丢弃旧账单', async () => {
    const pending = deferred<Awaited<ReturnType<typeof createPaidSubscription>>>(); vi.mocked(createPaidSubscription).mockReturnValue(pending.promise);
    const { wrapper } = await purchase({ subscriptionId: id(11), packageId: pkg.id }); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [tenant, body, signal] = vi.mocked(createPaidSubscription).mock.calls[0]!;
    await wrapper.setProps({ source: { subscriptionId: id(12), packageId: pkg.id } }); expect(signal?.aborted).toBe(true);
    pending.resolve({ ...offer, tenantId: tenant, subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' });
    await flushPromises(); expect(wrapper.emitted('created')).toBeUndefined(); wrapper.unmount();
  });
  it('同一历史来源重新渲染不会清除正在创建的账单', async () => {
    const pending = deferred<Awaited<ReturnType<typeof createPaidSubscription>>>(); vi.mocked(createPaidSubscription).mockReturnValue(pending.promise);
    const { wrapper } = await purchase({ subscriptionId: id(11), packageId: pkg.id }); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [tenant, body, signal] = vi.mocked(createPaidSubscription).mock.calls[0]!;
    await wrapper.setProps({ source: { subscriptionId: id(11), packageId: pkg.id } }); expect(signal?.aborted).toBe(false);
    pending.resolve({ ...offer, tenantId: tenant, subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' });
    await flushPromises(); expect(wrapper.emitted('created')).toHaveLength(1);
    await wrapper.setProps({ source: { subscriptionId: id(11), packageId: pkg.id } });
    expect(wrapper.text()).toContain(id(5)); expect(wrapper.find('[data-testid="confirm"]').exists()).toBe(false); wrapper.unmount();
  });
  it('关闭重开保留原报价、时间和请求身份，显式恢复不会产生新键', async () => {
    vi.mocked(createPaidSubscription).mockRejectedValueOnce(new Error('lost')).mockImplementationOnce(async (tenant, body) => ({ ...offer, tenantId: tenant, subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' }));
    const { wrapper } = await purchase(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const body = vi.mocked(createPaidSubscription).mock.calls[0]![1];
    expect(Object.isFrozen(body)).toBe(true); expect(wrapper.find('[data-testid="subscription-invoice-recovery"]').exists()).toBe(true);
    await wrapper.get('[data-testid="close"]').trigger('click'); await wrapper.get('[data-testid="paid-subscription-create"]').trigger('click');
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(createPaidSubscription).toHaveBeenCalledTimes(2); expect(vi.mocked(createPaidSubscription).mock.calls[1]![1]).toBe(body);
    expect(getPackageOffer).toHaveBeenCalledTimes(1); expect(wrapper.emitted('created')).toHaveLength(1); wrapper.unmount();
  });
  it.each(['tenant', 'permissions', 'close'] as const)('创建等待中发生 %s 变化会取消并丢弃迟到账单', async change => {
    const pending = deferred<Awaited<ReturnType<typeof createPaidSubscription>>>(); vi.mocked(createPaidSubscription).mockReturnValue(pending.promise);
    const { wrapper, session } = await purchase(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [tenant, body, signal] = vi.mocked(createPaidSubscription).mock.calls[0]!;
    if (change === 'tenant') await wrapper.setProps({ tenantId: id(8) });
    else if (change === 'permissions') session.currentUser!.permissions = [];
    else await wrapper.get('[data-testid="close"]').trigger('click');
    await flushPromises(); expect(signal?.aborted).toBe(true);
    pending.resolve({ ...offer, tenantId: tenant, subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' });
    await flushPromises(); expect(wrapper.emitted('created')).toBeUndefined(); expect(checkoutSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('报价属于其他套餐时不能创建账单', async () => {
    vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, packageId: id(8) }); const { wrapper } = await purchase();
    expect(wrapper.find('[data-testid="confirm"]').exists()).toBe(false); expect(createPaidSubscription).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('服务端明确报价冲突后先重读，再确认新报价和新请求身份', async () => {
    vi.mocked(createPaidSubscription).mockRejectedValue({ status: 409, code: 'tenancy.package_offer.version_conflict', title: 'changed' });
    const { wrapper } = await purchase(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const original = vi.mocked(createPaidSubscription).mock.calls[0]![1];
    vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, version: 4 });
    await wrapper.get('[data-testid="subscription-reconfirm-quote"]').trigger('click'); await flushPromises();
    expect(createPaidSubscription).toHaveBeenCalledTimes(1); expect(getPackageOffer).toHaveBeenCalledTimes(2);
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const changed = vi.mocked(createPaidSubscription).mock.calls[1]![1];
    expect(changed.offerVersion).toBe(4); expect(changed.idempotencyKey).not.toBe(original.idempotencyKey); wrapper.unmount();
  });
  it('错配租户账单不接入付款入口且不报创建成功', async () => {
    vi.mocked(createPaidSubscription).mockImplementation(async (_, body) => ({ ...offer, tenantId: id(8), subscriptionId: id(5), paymentOperationId: id(7), subject: 'Plan', offerVersion: body.offerVersion, effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2027-01-01T00:00:00Z' }));
    const { wrapper } = await purchase(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.emitted('created')).toBeUndefined(); expect(wrapper.find('[data-testid="subscription-checkout"]').exists()).toBe(false); wrapper.unmount();
  });
  it('下单未知后重开只提交原订阅，成功后不重复创建', async () => {
    vi.mocked(checkoutSubscription).mockRejectedValueOnce(new Error('lost')).mockResolvedValueOnce(checkout);
    const { pinia } = sessionSetup(); const wrapper = mount(SubscriptionCheckoutAction, { props: { tenantId: id(1), subscriptionId: id(5) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-checkout"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="close"]').trigger('click'); await wrapper.get('[data-testid="subscription-checkout"]').trigger('click');
    expect(wrapper.text()).toContain('下单结果未确认'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(checkoutSubscription).toHaveBeenCalledTimes(2); expect(vi.mocked(checkoutSubscription).mock.calls.every(call => call[0] === id(1) && call[1] === id(5))).toBe(true);
    expect(wrapper.find('[data-testid="confirm"]').exists()).toBe(false); wrapper.unmount();
  });
  it('下单响应错配订阅时不展示其他订单', async () => {
    vi.mocked(checkoutSubscription).mockResolvedValue({ ...checkout, subscriptionId: id(8) }); const { pinia } = sessionSetup();
    const wrapper = mount(SubscriptionCheckoutAction, { props: { tenantId: id(1), subscriptionId: id(5) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-checkout"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.text()).not.toContain('weixin://original'); wrapper.unmount();
  });
  it('履约重试未知时清除旧资格，先显式重读才允许再次提交', async () => {
    vi.mocked(retrySubscriptionFulfillment).mockRejectedValue(new Error('lost')); const { pinia } = sessionSetup();
    const wrapper = mount(SubscriptionFulfillmentAction, { props: { orderId: id(6) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-fulfillment"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="subscription-fulfillment-retry"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('履约结果未确认'); expect(wrapper.find('[data-testid="subscription-fulfillment-retry"]').exists()).toBe(false); expect(retrySubscriptionFulfillment).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="subscription-fulfillment-refresh"]').trigger('click'); await flushPromises();
    expect(getSubscriptionFulfillment).toHaveBeenCalledTimes(2); expect(wrapper.find('[data-testid="subscription-fulfillment-retry"]').exists()).toBe(true); wrapper.unmount();
  });
  it('其他订单的履约结果不接入当前订单', async () => {
    vi.mocked(getSubscriptionFulfillment).mockResolvedValue({ ...fulfillment, paymentOrderId: id(8) }); const { pinia } = sessionSetup();
    const wrapper = mount(SubscriptionFulfillmentAction, { props: { orderId: id(6) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-fulfillment"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.find('[data-testid="subscription-fulfillment-retry"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['target', 'permissions'] as const)('下单等待中发生 %s 变化不接入迟到支付链接', async change => {
    const pending = deferred<SubscriptionCheckoutResponse>(); vi.mocked(checkoutSubscription).mockReturnValue(pending.promise);
    const { pinia, session } = sessionSetup();
    const wrapper = mount(SubscriptionCheckoutAction, { props: { tenantId: id(1), subscriptionId: id(5) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-checkout"]').trigger('click'); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(checkoutSubscription).mock.calls[0]![2];
    if (change === 'target') await wrapper.setProps({ subscriptionId: id(8) }); else session.currentUser!.permissions = [];
    await flushPromises(); expect(signal?.aborted).toBe(true); pending.resolve(checkout); await flushPromises();
    expect(wrapper.text()).not.toContain('weixin://original'); expect(wrapper.find('[data-testid="subscription-fulfillment"]').exists()).toBe(false); wrapper.unmount();
  });
  it('只读履约用户可刷新但不能发起履约重试', async () => {
    const { pinia, session } = sessionSetup(); session.currentUser!.permissions = ['payments.orders.read', 'tenancy.tenant_subscriptions.read'];
    const wrapper = mount(SubscriptionFulfillmentAction, { props: { orderId: id(6) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-fulfillment"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="subscription-fulfillment-refresh"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="subscription-fulfillment-retry"]').exists()).toBe(false); expect(retrySubscriptionFulfillment).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('切换订单会取消履约读取，旧响应不恢复旧订单资格', async () => {
    const pending = deferred<typeof fulfillment>(); vi.mocked(getSubscriptionFulfillment).mockReturnValue(pending.promise);
    const { pinia } = sessionSetup(); const wrapper = mount(SubscriptionFulfillmentAction, { props: { orderId: id(6) }, global: globalFor(pinia) });
    await wrapper.get('[data-testid="subscription-fulfillment"]').trigger('click'); await flushPromises();
    const signal = vi.mocked(getSubscriptionFulfillment).mock.calls[0]![1]; await wrapper.setProps({ orderId: id(8) });
    expect(signal?.aborted).toBe(true); pending.resolve(fulfillment); await flushPromises();
    expect(wrapper.find('[data-testid="subscription-fulfillment-retry"]').exists()).toBe(false); expect(getSubscriptionFulfillment).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
});
