import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrder, PaymentOrderPage } from '@fullnet/client-contracts';
import PaymentOrdersView from './PaymentOrdersView.vue';
import { useSessionStore } from '../auth/session';
import { listPaymentOrders } from '../api/payment-orders';
vi.mock('vue-router', async original => ({ ...await original<typeof import('vue-router')>(), useRouter: () => ({ push: vi.fn() }) }));
vi.mock('../api/payment-orders', () => ({ listPaymentOrders: vi.fn(), getPaymentOrder: vi.fn(), createPaymentOrder: vi.fn(), reconcilePaymentOrder: vi.fn(), createPaymentRefund: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'succeeded', amountMinor: '29', currency: 'CNY', subject: 'Original' } as PaymentOrder;
const wrappers: VueWrapper[] = [];
async function panel() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions: ['payments.orders.read'] } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrdersView, { global: { plugins: [pinia], stubs: { PaymentOrderDetailDialog: true, SubscriptionFulfillmentAction: true } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function search(wrapper: VueWrapper) { await wrapper.get('form.payment-order-filters').trigger('submit'); await flushPromises(); }
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(listPaymentOrders).mockImplementation(async query => ({
    items: [{ ...order, tenantId: query?.tenantId ?? order.tenantId, tradeStateKey: query?.tradeStateKey ?? order.tradeStateKey }],
    page: query?.page ?? 1, pageSize: 20, total: 60 }));
});
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));

describe('订单来源筛选与分页', () => {
  it('筛选在来源生效，翻页沿已提交条件，重置才恢复全部', async () => {
    const { wrapper } = await panel();
    await wrapper.get('[data-testid="payment-orders-filter-tenant"]').setValue(id(4));
    await wrapper.get('[data-testid="payment-orders-filter-trade"]').setValue(' original ');
    wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'refunding'); await search(wrapper);
    expect(listPaymentOrders).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, tenantId: id(4), tradeStateKey: 'refunding', outTradeNoContains: 'original' }, expect.any(AbortSignal));
    await wrapper.get('[data-testid="payment-orders-filter-tenant"]').setValue(id(5));
    wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 2); await flushPromises();
    expect(vi.mocked(listPaymentOrders).mock.calls.at(-1)![0]).toEqual({ page: 2, pageSize: 20, tenantId: id(4), tradeStateKey: 'refunding', outTradeNoContains: 'original' });
    await wrapper.get('[data-testid="payment-orders-reset"]').trigger('click'); await flushPromises();
    expect(listPaymentOrders).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, tenantId: undefined, tradeStateKey: undefined, outTradeNoContains: undefined }, expect.any(AbortSignal));
  });
  it.each(['bad', '00000000-0000-0000-0000-000000000000'])('无效租户 %s 清空旧页，不回退全局或发送请求', async value => {
    const { wrapper } = await panel(); await wrapper.get('[data-testid="payment-orders-filter-tenant"]').setValue(value); await search(wrapper);
    expect(listPaymentOrders).toHaveBeenCalledTimes(1); expect(wrapper.text()).toContain('不会回退为全部订单');
    expect(wrapper.find('.el-table__body-wrapper').text()).not.toContain(order.outTradeNo);
    await wrapper.get('[data-testid="payment-orders-reset"]').trigger('click'); await flushPromises(); expect(listPaymentOrders).toHaveBeenCalledTimes(2);
  });
  it('未知状态和过长订单号条件不进入来源查询', async () => {
    const { wrapper } = await panel(); wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'unrecognized'); await search(wrapper);
    expect(listPaymentOrders).toHaveBeenCalledTimes(1);
    wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'all');
    await wrapper.get('[data-testid="payment-orders-filter-trade"]').setValue('A'.repeat(65)); await search(wrapper); expect(listPaymentOrders).toHaveBeenCalledTimes(1);
  });
  it.each(['tenant', 'state', 'page'])('来源 %s 错配拒绝整页，不在前端过滤成成功结果', async field => {
    const { wrapper } = await panel();
    vi.mocked(listPaymentOrders).mockResolvedValue({ items: [{ ...order, tradeStateKey: field === 'state' ? 'closed' : 'refunding', tenantId: field === 'tenant' ? id(5) : id(4) }],
      page: field === 'page' ? 2 : 1, pageSize: 20, total: 1 });
    await wrapper.get('[data-testid="payment-orders-filter-tenant"]').setValue(id(4)); wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'refunding'); await search(wrapper);
    expect(wrapper.text()).toContain('订单分页与已提交的筛选条件不一致'); expect(wrapper.find('.el-table__body-wrapper').text()).not.toContain(order.outTradeNo);
  });
  it('新筛选取消旧读取，旧结果不能覆盖新页', async () => {
    const { wrapper } = await panel(); let resolve!: (value: PaymentOrderPage) => void;
    vi.mocked(listPaymentOrders).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    await wrapper.get('[data-testid="payment-orders-filter-tenant"]').setValue(id(4)); await search(wrapper);
    const signal = vi.mocked(listPaymentOrders).mock.calls[1]![1]!;
    await wrapper.get('[data-testid="payment-orders-reset"]').trigger('click'); await flushPromises();
    resolve({ items: [{ ...order, tenantId: id(4), outTradeNo: 'old-filter-order' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper.text()).toContain(order.outTradeNo); expect(wrapper.text()).not.toContain('old-filter-order');
  });
});
