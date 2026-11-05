import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrder } from '@fullnet/client-contracts';
import PaymentOrderReconcileDialog from './PaymentOrderReconcileDialog.vue';
import { useSessionStore } from '../auth/session';
import { getPaymentOrder, reconcilePaymentOrder } from '../api/payment-orders';
vi.mock('../api/payment-orders', () => ({ getPaymentOrder: vi.fn(), reconcilePaymentOrder: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const order: PaymentOrder = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'provider_unknown', amountMinor: '9223372036854775807', currency: 'CNY', subject: 'Original', description: null,
  codeUrl: null, providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 7 };
const dialog = { props: ['open', 'confirmTestId', 'confirmDisabled', 'saving'], emits: ['confirm', 'update:open'],
  template: '<section v-if="open"><slot/><button data-testid="reconcile-test-close" @click="$emit(\'update:open\', false)">Close</button><button :data-testid="confirmTestId" :disabled="confirmDisabled || saving" @click="$emit(\'confirm\')">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.orders.read', 'payments.orders.reconcile'], reference = order) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrderReconcileDialog, { props: { open: true, reference }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function send(wrapper: VueWrapper) { await wrapper.get('[data-testid="payment-reconcile-confirm"]').trigger('click'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getPaymentOrder).mockResolvedValue({ ...order });
  vi.mocked(reconcilePaymentOrder).mockResolvedValue({ ...order, tradeStateKey: 'awaiting_payment', version: 8 }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('人工对账原订单确认', () => {
  it('打开只读原订单；关闭不会对账', async () => {
    const { wrapper } = await panel(); expect(getPaymentOrder).toHaveBeenCalledExactlyOnceWith(order.id, expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="payment-reconcile-snapshot"]').text()).toContain('92233720368547758.07');
    await wrapper.get('[data-testid="reconcile-test-close"]').trigger('click'); expect(reconcilePaymentOrder).not.toHaveBeenCalled();
  });
  it('确认固定原订单，重复点击一次，回执仅显示实际状态并锁定下一次确认', async () => {
    let resolve!: (value: PaymentOrder) => void; vi.mocked(reconcilePaymentOrder).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper } = await panel(); await send(wrapper); await send(wrapper);
    expect(reconcilePaymentOrder).toHaveBeenCalledExactlyOnceWith(order.id, expect.any(AbortSignal));
    resolve({ ...order, tradeStateKey: 'awaiting_payment', version: 8 }); await flushPromises();
    expect(wrapper.emitted('updated')).toHaveLength(1); expect(wrapper.get('[data-testid="payment-reconcile-confirm"]').attributes('disabled')).toBeDefined();
    expect(wrapper.get('[data-testid="payment-reconcile-snapshot"]').text()).toContain('待支付');
  });
  it('未知后关闭重开不自动查询或重发，必须显式重读再确认', async () => {
    vi.mocked(reconcilePaymentOrder).mockRejectedValueOnce(new Error('lost'));
    const { wrapper } = await panel(); await send(wrapper); await wrapper.get('[data-testid="reconcile-test-close"]').trigger('click');
    await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises();
    expect(getPaymentOrder).toHaveBeenCalledTimes(1); expect(reconcilePaymentOrder).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="payment-reconcile-confirm"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="payment-reconcile-read"]').trigger('click'); await flushPromises(); await send(wrapper);
    expect(getPaymentOrder).toHaveBeenCalledTimes(2); expect(reconcilePaymentOrder).toHaveBeenCalledTimes(2); expect(wrapper.emitted('updated')).toHaveLength(1);
  });
  it.each(['id', 'tenantId', 'merchantConfigId', 'channelKey', 'outTradeNo', 'amountMinor', 'currency'])('读取 %s 错配不能确认', async field => {
    vi.mocked(getPaymentOrder).mockResolvedValue({ ...order, [field]: field.endsWith('Id') || field === 'id' ? id(6) : 'wrong' });
    const { wrapper } = await panel(); await send(wrapper); expect(reconcilePaymentOrder).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="payment-reconcile-snapshot"]').exists()).toBe(false); expect(wrapper.find('[data-testid="payment-reconcile-problem"]').exists()).toBe(true);
  });
  it('回执版本倒退不能宣称成功', async () => {
    vi.mocked(reconcilePaymentOrder).mockResolvedValue({ ...order, version: 6 }); const { wrapper } = await panel(); await send(wrapper);
    expect(wrapper.emitted('updated')).toBeUndefined(); expect(wrapper.find('[data-testid="payment-reconcile-unconfirmed"]').exists()).toBe(true);
  });
  it('同一 UUID 大小写变化不能绕过未知结果重读要求', async () => {
    vi.mocked(reconcilePaymentOrder).mockRejectedValue(new Error('lost')); const { wrapper } = await panel(); await send(wrapper);
    await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true, reference: { ...order, id: order.id.toUpperCase() } }); await flushPromises();
    expect(getPaymentOrder).toHaveBeenCalledTimes(1); expect(wrapper.get('[data-testid="payment-reconcile-confirm"]').attributes('disabled')).toBeDefined();
  });
  it.each([{ ...order, tradeStateKey: 'refunded' }, { ...order, tradeStateKey: 'refunding', channelKey: 'alipay_page' }])('不支持的原状态或退款渠道只能读取', async value => {
    vi.mocked(getPaymentOrder).mockResolvedValue(value); const { wrapper } = await panel(undefined, value); await send(wrapper);
    expect(reconcilePaymentOrder).not.toHaveBeenCalled();
  });
  it.each([['payments.orders.read'], ['payments.orders.reconcile']])('缺少一项独立权限不能打开', async permission => {
    const { wrapper } = await panel([permission]); expect(getPaymentOrder).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="payment-reconcile-confirm"]').exists()).toBe(false);
  });
  it.each(['account', 'session', 'tenant', 'permission'])('%s 改变取消本地观察，迟到成功不接入', async change => {
    let resolve!: (value: PaymentOrder) => void; vi.mocked(reconcilePaymentOrder).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await send(wrapper); const signal = vi.mocked(reconcilePaymentOrder).mock.calls[0]![1]!;
    if (change === 'account') session.currentUser!.id = id(7); else if (change === 'session') session.currentUser!.sessionId = id(7);
    else if (change === 'tenant') session.currentUser!.tenantId = id(7); else session.currentUser!.permissions = ['payments.orders.read'];
    await flushPromises(); expect(signal.aborted).toBe(true); resolve({ ...order, version: 8 }); await flushPromises();
    expect(wrapper.emitted('updated')).toBeUndefined(); expect(wrapper.find('[data-testid="payment-reconcile-snapshot"]').exists()).toBe(false);
  });
  it('重读失败清除旧快照，不能用旧版本继续确认', async () => {
    const { wrapper } = await panel(); vi.mocked(getPaymentOrder).mockRejectedValue(new Error('failed'));
    await wrapper.get('[data-testid="payment-reconcile-read"]').trigger('click'); await flushPromises(); await send(wrapper);
    expect(wrapper.find('[data-testid="payment-reconcile-snapshot"]').exists()).toBe(false); expect(reconcilePaymentOrder).not.toHaveBeenCalled();
  });
  it('切换目标取消读取，旧订单不能覆盖新目标', async () => {
    let resolve!: (value: PaymentOrder) => void; vi.mocked(getPaymentOrder).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    const { wrapper } = await panel(); const signal = vi.mocked(getPaymentOrder).mock.calls[0]![1]!;
    vi.mocked(getPaymentOrder).mockResolvedValue({ ...order, id: id(6) }); await wrapper.setProps({ reference: { ...order, id: id(6) } }); await flushPromises();
    expect(signal.aborted).toBe(true); resolve(order); await flushPromises(); await send(wrapper);
    expect(reconcilePaymentOrder).toHaveBeenCalledExactlyOnceWith(id(6), expect.any(AbortSignal));
  });
});
