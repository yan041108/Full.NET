import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrder } from '@fullnet/client-contracts';
import PaymentOrderDetailDialog from './PaymentOrderDetailDialog.vue';
import { useSessionStore } from '../auth/session';
import { getPaymentOrder } from '../api/payment-orders';
vi.mock('../api/payment-orders', () => ({ getPaymentOrder: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const order: PaymentOrder = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'succeeded', amountMinor: '29', currency: 'CNY', subject: 'Original', description: null, codeUrl: 'https://example.com/payment-link',
  providerTransactionId: 'provider-transaction', failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 1 };
const dialog = { props: ['open'], emits: ['update:open'], template: '<section v-if="open"><slot/><button data-testid="close-detail" @click="$emit(\'update:open\', false)">Close</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.orders.read'], tenantId: string | null = null, orderId = order.id, reference?: typeof order) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrderDetailDialog, { props: { open: true, orderId, reference }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getPaymentOrder).mockResolvedValue(order); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));

describe('原支付订单只读详情', () => {
  it('独立按精确 ID 读取，金额精确、支付链接仅作文本', async () => {
    const { wrapper } = await panel();
    expect(getPaymentOrder).toHaveBeenCalledExactlyOnceWith(order.id, expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="payment-order-detail-amount"]').text()).toBe('0.29 CNY');
    expect(wrapper.text()).toContain(order.codeUrl); expect(wrapper.find('a').exists()).toBe(false);
  });
  it('退款读取权限不授予订单读取，Tenant 上下文也不读取', async () => {
    await panel(['payments.refunds.read']); await panel(['payments.orders.read'], id(2)); expect(getPaymentOrder).not.toHaveBeenCalled();
  });
  it.each(['bad', '00000000-0000-0000-0000-000000000000'])('无效 ID %s 不发送请求', async value => {
    await panel(undefined, null, value); expect(getPaymentOrder).not.toHaveBeenCalled();
  });
  it.each([503, 403, 404])('刷新 %s 清空快照并保留原请求 ID', async status => {
    const { wrapper } = await panel(); vi.mocked(getPaymentOrder).mockRejectedValue({ type: 'about:blank', status, code: 'test.failed', title: 'Not available' });
    await wrapper.get('[data-testid="payment-order-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="payment-order-detail-snapshot"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="payment-order-detail-id"]').text()).toBe(order.id);
    expect(wrapper.get('[data-testid="payment-order-detail-problem"]').text()).toContain('Not available');
  });
  it.each(['tenantId', 'merchantConfigId', 'outTradeNo', 'currency'])('原退款的 %s 归属错配不展示另一笔订单', async field => {
    await panel(undefined, null, order.id, { ...order, [field]: field.endsWith('Id') ? id(6) : 'different' });
    const wrapper = wrappers.at(-1)!;
    expect(wrapper.find('[data-testid="payment-order-detail-snapshot"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('订单响应与请求或原退款归属不一致');
  });
  it('响应 ID 错配拒绝展示；完整 Int64 金额精确读取', async () => {
    vi.mocked(getPaymentOrder).mockResolvedValueOnce({ ...order, id: id(6) }); const a = await panel();
    expect(a.wrapper.find('[data-testid="payment-order-detail-snapshot"]').exists()).toBe(false);
    vi.mocked(getPaymentOrder).mockResolvedValueOnce({ ...order, amountMinor: '9223372036854775807' }); const b = await panel();
    expect(b.wrapper.get('[data-testid="payment-order-detail-amount"]').text()).toBe('92233720368547758.07 CNY');
  });
  it('切换原订单取消并丢弃迟到响应', async () => {
    let resolve!: (value: PaymentOrder) => void;
    vi.mocked(getPaymentOrder).mockReturnValueOnce(new Promise(done => { resolve = done; })).mockResolvedValueOnce({ ...order, id: id(6), outTradeNo: 'new-order' });
    const { wrapper } = await panel(); const signal = vi.mocked(getPaymentOrder).mock.calls[0]![1]!;
    await wrapper.setProps({ orderId: id(6) }); await flushPromises(); resolve(order); await flushPromises();
    expect(signal.aborted).toBe(true); expect(wrapper.text()).toContain('new-order'); expect(wrapper.text()).not.toContain('original-order');
  });
  it.each(['account', 'session', 'tenant', 'permission'])('%s 改变关闭并取消，原 ID 不自动发送给新上下文', async change => {
    let resolve!: (value: PaymentOrder) => void; vi.mocked(getPaymentOrder).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); const signal = vi.mocked(getPaymentOrder).mock.calls[0]![1]!;
    if (change === 'account') session.currentUser!.id = id(6);
    else if (change === 'session') session.currentUser!.sessionId = id(6);
    else if (change === 'tenant') session.currentUser!.tenantId = id(6);
    else session.currentUser!.permissions = [];
    await flushPromises(); resolve(order); await flushPromises();
    expect(signal.aborted).toBe(true); expect(getPaymentOrder).toHaveBeenCalledTimes(1);
    expect(wrapper.find('[data-testid="payment-order-detail-snapshot"]').exists()).toBe(false);
  });
});
