import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { TenantSubscriptionInvoiceResponse } from '@fullnet/client-contracts';
import SubscriptionInvoiceDetails from './SubscriptionInvoiceDetails.vue';
import { getSubscriptionInvoice } from '../api/subscription-billing';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
vi.mock('../api/subscription-billing', () => ({ getSubscriptionInvoice: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const original: TenantSubscriptionInvoiceResponse = { subscriptionId: id(2), tenantId: id(1), packageId: id(3), subject: '原冻结商品', offerVersion: 7,
  amountMinor: '9007199254740993', currency: 'CNY', seatLimit: '9223372036854775807', storageLimit: '9223372036854775806',
  effectiveFromUtc: '2026-09-01T00:00:00Z', effectiveToUtc: '2026-10-01T00:00:00Z', phase: 'Refunded',
  renewalOfSubscriptionId: id(4), renewalSourceVersion: 3, paymentOrderId: id(5), readAtUtc: '2026-11-05T00:00:00Z' };
function setup() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'reader', displayName: 'Reader', actorScope: 'host', scope: 'host', tenantId: null,
    sessionId: id(10), permissions: ['tenancy.tenant_subscriptions.read'], isSuperAdministrator: false, passwordChangeRequired: false, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(SubscriptionInvoiceDetails, { props: { tenantId: id(1), subscriptionId: id(2) }, global: { plugins: [pinia] } });
  return { wrapper, session };
}
async function read(wrapper: ReturnType<typeof setup>['wrapper']) { await wrapper.get('[data-testid="subscription-invoice-read"]').trigger('click'); await flushPromises(); }
describe('原冻结账单只读回读', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(getSubscriptionInvoice).mockResolvedValue(original); });
  it('只有 read 权限即可显式读取，超安全整数金额和历史来源保持原值', async () => {
    const { wrapper } = setup(); expect(getSubscriptionInvoice).not.toHaveBeenCalled(); await read(wrapper);
    expect(wrapper.get('[data-testid="invoice-exact-amount"]').text()).toBe('9007199254740993');
    const snapshot = wrapper.get('[data-testid="subscription-invoice-snapshot"]');
    for (const exact of [original.subject, original.seatLimit, original.storageLimit, original.renewalOfSubscriptionId!, original.effectiveFromUtc, original.readAtUtc]) expect(snapshot.text()).toContain(exact);
    expect(snapshot.text()).toContain('已接收退款事实'); wrapper.unmount();
  });
  it('有效来源只发出原 ID 的只读选择，不自动再次读取账单', async () => {
    const { wrapper } = setup(); await read(wrapper); await wrapper.get('[data-testid="subscription-invoice-source"]').trigger('click');
    expect(wrapper.emitted('readSource')).toEqual([[original.renewalOfSubscriptionId]]); expect(getSubscriptionInvoice).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it.each([null, original.subscriptionId, '00000000-0000-0000-0000-000000000000'])('普通/自指/空来源 %s 不提供导航', async source => {
    vi.mocked(getSubscriptionInvoice).mockResolvedValue({ ...original, renewalOfSubscriptionId: source, renewalSourceVersion: source ? 3 : null });
    const { wrapper } = setup(); await read(wrapper); expect(wrapper.find('[data-testid="subscription-invoice-source"]').exists()).toBe(false);
    expect(wrapper.emitted('readSource')).toBeUndefined(); wrapper.unmount();
  });
  it('账单刷新期间禁用来源入口', async () => {
    const { wrapper } = setup(); await read(wrapper); const pending = Promise.withResolvers<TenantSubscriptionInvoiceResponse>();
    vi.mocked(getSubscriptionInvoice).mockReturnValue(pending.promise); await read(wrapper);
    expect(wrapper.get('[data-testid="subscription-invoice-source"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="subscription-invoice-source"]').trigger('click'); expect(wrapper.emitted('readSource')).toBeUndefined();
    pending.resolve(original); await flushPromises(); wrapper.unmount();
  });
  it('刷新临时失败保留原快照及原读取时间', async () => {
    const { wrapper } = setup(); await read(wrapper); vi.mocked(getSubscriptionInvoice).mockRejectedValue({ status: 503, title: 'temporary' }); await read(wrapper);
    expect(wrapper.get('[data-testid="subscription-invoice-failure"]').text()).toContain('保留');
    expect(wrapper.get('[data-testid="subscription-invoice-snapshot"]').text()).toContain(original.readAtUtc); wrapper.unmount();
  });
  it.each([401, 403, 404])('明确拒绝 %s 清除之前的账单', async status => {
    const { wrapper } = setup(); await read(wrapper); vi.mocked(getSubscriptionInvoice).mockRejectedValue({ status, code: 'test.denied', title: 'denied' }); await read(wrapper);
    expect(wrapper.find('[data-testid="subscription-invoice-snapshot"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['tenantId', 'subscriptionId'] as const)('错配 %s 响应不接入当前详情', async key => {
    vi.mocked(getSubscriptionInvoice).mockResolvedValue({ ...original, [key]: id(99) }); const { wrapper } = setup(); await read(wrapper);
    expect(wrapper.find('[data-testid="subscription-invoice-snapshot"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['01', '-1', '9223372036854775808'])('拒绝非规范或超 Int64 的金额 %s', async amountMinor => {
    vi.mocked(getSubscriptionInvoice).mockResolvedValue({ ...original, amountMinor }); const { wrapper } = setup(); await read(wrapper);
    expect(wrapper.find('[data-testid="subscription-invoice-snapshot"]').exists()).toBe(false); wrapper.unmount();
  });
  it('来源切换取消晚到请求并丢弃旧结果', async () => {
    const pending = Promise.withResolvers<TenantSubscriptionInvoiceResponse>(); vi.mocked(getSubscriptionInvoice).mockReturnValue(pending.promise);
    const { wrapper } = setup(); await read(wrapper); const signal = vi.mocked(getSubscriptionInvoice).mock.calls[0]![2];
    await wrapper.setProps({ subscriptionId: id(6) }); expect(signal?.aborted).toBe(true);
    pending.resolve(original); await flushPromises(); expect(wrapper.find('[data-testid="subscription-invoice-snapshot"]').exists()).toBe(false); wrapper.unmount();
  });
  it('撤权清除已读取的原账单且忽略正在刷新的结果', async () => {
    const { wrapper, session } = setup(); await read(wrapper);
    const pending = Promise.withResolvers<TenantSubscriptionInvoiceResponse>(); vi.mocked(getSubscriptionInvoice).mockReturnValue(pending.promise); await read(wrapper);
    const signal = vi.mocked(getSubscriptionInvoice).mock.calls[1]![2]; session.currentUser!.permissions = []; await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve(original); await flushPromises(); expect(wrapper.text()).toBe(''); wrapper.unmount();
  });
  it('读取中重复点击不产生第二次请求，离开页面取消', async () => {
    const pending = Promise.withResolvers<TenantSubscriptionInvoiceResponse>(); vi.mocked(getSubscriptionInvoice).mockReturnValue(pending.promise);
    const { wrapper } = setup(); wrapper.getComponent({ name: 'ElButton' }).vm.$emit('click'); wrapper.getComponent({ name: 'ElButton' }).vm.$emit('click'); await flushPromises();
    expect(getSubscriptionInvoice).toHaveBeenCalledTimes(1); const signal = vi.mocked(getSubscriptionInvoice).mock.calls[0]![2]; wrapper.unmount(); expect(signal?.aborted).toBe(true);
    pending.resolve(original); await flushPromises();
  });
});
