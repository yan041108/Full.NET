import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrder, PaymentRefund } from '@fullnet/client-contracts';
import PaymentOrdersView from './PaymentOrdersView.vue';
import { useSessionStore } from '../auth/session';
import { createPaymentRefund, listPaymentOrders } from '../api/payment-orders';

vi.mock('vue-router', async original => ({ ...await original<typeof import('vue-router')>(), useRouter: () => ({ push: vi.fn() }) }));
vi.mock('../api/payment-orders', () => ({ listPaymentOrders: vi.fn(), createPaymentOrder: vi.fn(), getPaymentOrder: vi.fn(),
  reconcilePaymentOrder: vi.fn(), createPaymentRefund: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'succeeded', amountMinor: '100', currency: 'CNY', subject: '原订单', version: 1 } as PaymentOrder;
const receipt = { id: id(4), orderId: order.id, tenantId: order.tenantId, merchantConfigId: order.merchantConfigId,
  outTradeNo: order.outTradeNo, outRefundNo: 'original-refund', amountMinor: '29', currency: 'CNY', reason: 'original',
  refundStateKey: 'processing' } as PaymentRefund;
const dialog = { props: ['open', 'confirmTestId', 'confirmDisabled', 'saving'], emits: ['confirm', 'update:open'],
  template: '<section v-if="open"><slot/><button data-testid="close-refund-test" @click="$emit(\'update:open\', false)">Close</button><button :data-testid="confirmTestId" :disabled="confirmDisabled || saving" @click="$emit(\'confirm\')">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), tenantId: null, actorScope: 'host', sessionId: id(8), permissions: ['payments.orders.read', 'payments.refunds.create'] } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrdersView, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog, SubscriptionFulfillmentAction: true } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function start(wrapper: VueWrapper) {
  await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
  await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue(' original ');
  await wrapper.get('[data-testid="payment-order-refund-amount"]').setValue('0.29');
  await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(listPaymentOrders).mockResolvedValue({ items: [order], page: 1, pageSize: 20, total: 1 });
  vi.mocked(createPaymentRefund).mockResolvedValue(receipt);
});
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));

describe('退款原请求保护', () => {
  it('发出请求即冻结，双击只发一次；未知后关闭重开仍是原金额原因', async () => {
    let reject!: (error: Error) => void;
    vi.mocked(createPaymentRefund).mockReturnValue(new Promise((_, fail) => { reject = fail; }));
    const { wrapper } = await panel(); await start(wrapper);
    expect(wrapper.get('[data-testid="payment-order-refund-amount"]').attributes('readonly')).toBeDefined();
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click');
    expect(createPaymentRefund).toHaveBeenCalledTimes(1);
    reject(new Error('response lost')); await flushPromises();
    expect(wrapper.get('[data-testid="payment-refund-original-attempt"]').text()).toContain(order.id);
    await wrapper.get('[data-testid="close-refund-test"]').trigger('click');
    await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    expect((wrapper.get('[data-testid="payment-order-refund-reason"]').element as HTMLTextAreaElement).value).toBe('original');
    expect((wrapper.get('[data-testid="payment-order-refund-amount"]').element as HTMLInputElement).value).toBe('0.29');
    expect(wrapper.get('[data-testid="payment-order-refund-confirm"]').attributes('disabled')).toBeDefined();
    expect(wrapper.find('[data-testid="payment-refund-check-records"]').exists()).toBe(false);
    expect(createPaymentRefund).toHaveBeenCalledWith(order.id, { amountMinor: '29', reason: 'original' }, expect.any(AbortSignal));
  });

  it('有效回执只表示已提交，关闭重开不重复发送', async () => {
    const { wrapper } = await panel(); await start(wrapper);
    expect(wrapper.find('[data-testid="payment-order-refund-amount"]').exists()).toBe(false);
    expect(listPaymentOrders).toHaveBeenCalledTimes(2);
    await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    expect(wrapper.get('[data-testid="payment-order-refund-confirm"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click');
    expect(createPaymentRefund).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['id', '00000000-0000-0000-0000-000000000000'], ['orderId', id(5)], ['tenantId', id(5)], ['merchantConfigId', id(5)],
    ['outTradeNo', 'other'], ['amountMinor', '30'], ['currency', 'USD'], ['reason', 'other'], ['outRefundNo', ''], ['refundStateKey', 'unknown']
  ])('回执 %s 错配保留原请求，不冒充成功或再次写入', async (field, value) => {
    vi.mocked(createPaymentRefund).mockResolvedValue({ ...receipt, [field]: value });
    const { wrapper } = await panel(); await start(wrapper);
    expect(wrapper.get('[data-testid="payment-refund-attempt-status"]').text()).toContain('退款结果尚未核实');
    expect(wrapper.get('[data-testid="payment-order-refund-confirm"]').attributes('disabled')).toBeDefined();
    expect(listPaymentOrders).toHaveBeenCalledTimes(1); expect(createPaymentRefund).toHaveBeenCalledTimes(1);
  });

  it.each(['account', 'session', 'tenant', 'permission', 'anonymous'])('%s 改变取消并清除原请求，迟到回执不接入', async change => {
    let resolve!: (value: PaymentRefund) => void;
    vi.mocked(createPaymentRefund).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await start(wrapper);
    const signal = vi.mocked(createPaymentRefund).mock.calls[0]![2]!;
    if (change === 'account') session.currentUser!.id = id(6);
    else if (change === 'session') session.currentUser!.sessionId = id(6);
    else if (change === 'tenant') session.currentUser!.tenantId = id(6);
    else if (change === 'permission') session.currentUser!.permissions = ['payments.orders.read'];
    else session.state = 'anonymous';
    await flushPromises(); expect(signal.aborted).toBe(true); resolve(receipt); await flushPromises();
    expect(wrapper.find('[data-testid="payment-refund-original-attempt"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="payment-order-refund-amount"]').exists()).toBe(false);
    expect(listPaymentOrders).toHaveBeenCalledTimes(change === 'tenant' || change === 'anonymous' ? 1 : 2);
  });

  it('新会话列表先返回时，旧会话的迟到列表不能覆盖', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof listPaymentOrders>>) => void;
    vi.mocked(listPaymentOrders).mockReturnValueOnce(new Promise(done => { resolve = done; }))
      .mockResolvedValueOnce({ items: [{ ...order, outTradeNo: 'new-session-order' }], page: 1, pageSize: 20, total: 1 });
    const { wrapper, session } = await panel(); const signal = vi.mocked(listPaymentOrders).mock.calls[0]![1]!;
    session.currentUser!.sessionId = id(6); await flushPromises();
    expect(signal.aborted).toBe(true);
    resolve({ items: [{ ...order, outTradeNo: 'old-session-order' }], page: 1, pageSize: 20, total: 1 }); await flushPromises();
    expect(wrapper.text()).toContain('new-session-order'); expect(wrapper.text()).not.toContain('old-session-order');
  });

  it('离开页面取消原请求观察，迟到回执不能再刷新列表', async () => {
    let resolve!: (value: PaymentRefund) => void;
    vi.mocked(createPaymentRefund).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper } = await panel(); await start(wrapper);
    const signal = vi.mocked(createPaymentRefund).mock.calls[0]![2]!; wrapper.unmount();
    expect(signal.aborted).toBe(true); resolve(receipt); await flushPromises(); expect(listPaymentOrders).toHaveBeenCalledTimes(1);
  });

  it('大于原订单的金额在发送前拒绝，不锁定未发出的正文', async () => {
    const { wrapper } = await panel(); await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue('original');
    await wrapper.get('[data-testid="payment-order-refund-amount"]').setValue('1.01');
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
    expect(createPaymentRefund).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="payment-refund-original-attempt"]').exists()).toBe(false);
  });

  it('错误线格式的原订单金额不能以空金额发送全额退款', async () => {
    vi.mocked(listPaymentOrders).mockResolvedValue({ items: [{ ...order, amountMinor: '9223372036854775808' }], page: 1, pageSize: 20, total: 1 });
    const { wrapper } = await panel(); await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue('original');
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
    expect(createPaymentRefund).not.toHaveBeenCalled();
  });
});
