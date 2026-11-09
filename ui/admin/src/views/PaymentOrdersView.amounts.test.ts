import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrder, PaymentRefund } from '@fullnet/client-contracts';
import PaymentOrdersView from './PaymentOrdersView.vue';
import { useSessionStore } from '../auth/session';
import { createPaymentOrder, createPaymentRefund, listPaymentOrders } from '../api/payment-orders';
const navigation = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock('vue-router', async original => ({ ...await original<typeof import('vue-router')>(), useRouter: () => ({ push: navigation.push }) }));
vi.mock('../api/payment-orders', () => ({ listPaymentOrders: vi.fn(), createPaymentOrder: vi.fn(), getPaymentOrder: vi.fn(),
  reconcilePaymentOrder: vi.fn(), createPaymentRefund: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'paid-order', tradeStateKey: 'succeeded',
  amountMinor: '9223372036854775807', currency: 'CNY', codeUrl: null } as PaymentOrder;
const dialog = { props: ['open', 'confirmTestId'], emits: ['confirm'], template: '<section v-if="open"><slot/><button :data-testid="confirmTestId" @click="$emit(\'confirm\')">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(refundPermission = 'payments.refunds.create') {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), tenantId: null, actorScope: 'host', sessionId: id(8), permissions: ['payments.orders.read', 'payments.orders.create', refundPermission] } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrdersView, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog, SubscriptionFulfillmentAction: true } } });
  wrappers.push(wrapper); await flushPromises(); return wrapper;
}
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(listPaymentOrders).mockResolvedValue({ items: [order], page: 1, pageSize: 20, total: 1 });
  vi.mocked(createPaymentOrder).mockResolvedValue(order);
  vi.mocked(createPaymentRefund).mockResolvedValue({ id: id(4), orderId: order.id } as PaymentRefund);
});
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('支付页面金额提交', () => {
  it.each(['alipay_page', 'unknown', ''])('渠道 %s 不打开退款窗口，仍可只读核对', async channelKey => {
    vi.mocked(listPaymentOrders).mockResolvedValue({ items: [{ ...order, channelKey }], page: 1, pageSize: 20, total: 1 });
    const wrapper = await panel(); useSessionStore().currentUser!.permissions.push('payments.refunds.read'); await flushPromises();
    const action = wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`);
    expect(action.attributes('disabled')).toBeDefined(); await action.trigger('click');
    expect(wrapper.find('[data-testid="payment-order-refund-reason"]').exists()).toBe(false);
    expect(createPaymentRefund).not.toHaveBeenCalled();
    await wrapper.get(`[data-testid="payment-order-refund-records-${order.id}"]`).trigger('click');
    expect(navigation.push).toHaveBeenCalledWith({ name: 'payments-refunds', query: { orderId: order.id } });
  });
  it('退款结果未确认时可按原订单跳转只读记录，不再次提交退款', async () => {
    const wrapper = await panel(); useSessionStore().currentUser!.permissions.push('payments.refunds.read'); await flushPromises();
    vi.mocked(createPaymentRefund).mockRejectedValue(new Error('unconfirmed'));
    await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue('original');
    await wrapper.get('[data-testid="payment-order-refund-amount"]').setValue('0.29');
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="payment-refund-check-records"]').trigger('click'); await flushPromises();
    expect(navigation.push).toHaveBeenCalledWith({ name: 'payments-refunds', query: { orderId: order.id } });
    expect(createPaymentRefund).toHaveBeenCalledTimes(1); expect(wrapper.find('[data-testid="payment-order-refund-amount"]').exists()).toBe(false);
  });
  it('退款入口只认后端现有精确权限，不接受不存在的旧权限码', async () => {
    const wrapper = await panel('payments.orders.refund');
    expect(wrapper.find(`[data-testid="payment-order-refund-${order.id}"]`).exists()).toBe(false);
  });
  it.each(['0.29', '90071992547409.91', '92233720368547758.07'])('创建金额 %s 只按精确分提交', async amount => {
    const wrapper = await panel(); await wrapper.get('[data-testid="payment-order-create"]').trigger('click');
    await wrapper.get('[data-testid="payment-order-create-tenant"]').setValue(id(2));
    await wrapper.get('[data-testid="payment-order-create-subject"]').setValue('明确订单');
    await wrapper.get('[data-testid="payment-order-create-amount"]').setValue(amount);
    await wrapper.get('[data-testid="payment-order-create-confirm"]').trigger('click'); await flushPromises();
    expect(vi.mocked(createPaymentOrder).mock.calls[0]![0].amountMinor).toBe(amount === '0.29' ? '29' : amount === '92233720368547758.07' ? '9223372036854775807' : '9007199254740991');
  });
  it('并列渠道选择保留正确请求身份，指定商户时渠道选择禁用', async () => {
    const wrapper = await panel(); await wrapper.get('[data-testid="payment-order-create"]').trigger('click');
    await wrapper.get('[data-testid="payment-order-create-tenant"]').setValue(id(2));
    await wrapper.get('[data-testid="payment-order-create-subject"]').setValue('明确订单');
    await wrapper.get('[data-testid="payment-order-create-amount"]').setValue('0.29');
    wrapper.getComponent({ name: 'ElRadioGroup' }).vm.$emit('update:modelValue', 'alipay_page'); await flushPromises();
    await wrapper.get('[data-testid="payment-order-create-confirm"]').trigger('click'); await flushPromises();
    expect(vi.mocked(createPaymentOrder).mock.calls[0]![0].channelKey).toBe('alipay_page');
    await wrapper.get('[data-testid="payment-order-create"]').trigger('click');
    await wrapper.get('[data-testid="payment-order-create-merchant"]').setValue(id(3));
    expect(wrapper.getComponent({ name: 'ElRadioGroup' }).props('disabled')).toBe(true);
  });
  it.each(['1abc', '1e3', '1.001', '92233720368547758.08', '92233720368547758.071'])('创建非法金额 %s 不调用接口', async amount => {
    const wrapper = await panel(); await wrapper.get('[data-testid="payment-order-create"]').trigger('click');
    await wrapper.get('[data-testid="payment-order-create-tenant"]').setValue(id(2));
    await wrapper.get('[data-testid="payment-order-create-subject"]').setValue('明确订单');
    await wrapper.get('[data-testid="payment-order-create-amount"]').setValue(amount);
    await wrapper.get('[data-testid="payment-order-create-confirm"]').trigger('click'); await flushPromises();
    expect(createPaymentOrder).not.toHaveBeenCalled();
  });
  it.each(['', '0.29', '90071992547409.91', '92233720368547758.07'])('退款金额 %s 保留空金额语义或精确分', async amount => {
    const wrapper = await panel(); await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue('明确退款原因');
    await wrapper.get('[data-testid="payment-order-refund-amount"]').setValue(amount);
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
    expect(createPaymentRefund).toHaveBeenCalledWith(order.id, { reason: '明确退款原因', amountMinor: amount === '' ? null : amount === '0.29' ? '29' : amount === '92233720368547758.07' ? '9223372036854775807' : '9007199254740991' }, expect.any(AbortSignal));
  });
  it.each(['0', '1abc', '1e3', '2.675', '92233720368547758.08', '92233720368547758.071'])('退款非法金额 %s 不调用接口', async amount => {
    const wrapper = await panel(); await wrapper.get(`[data-testid="payment-order-refund-${order.id}"]`).trigger('click');
    await wrapper.get('[data-testid="payment-order-refund-reason"]').setValue('明确退款原因');
    await wrapper.get('[data-testid="payment-order-refund-amount"]').setValue(amount);
    await wrapper.get('[data-testid="payment-order-refund-confirm"]').trigger('click'); await flushPromises();
    expect(createPaymentRefund).not.toHaveBeenCalled();
  });
  it('列表边界金额按整数展示，超界数值不伪装为可核对金额', async () => {
    const wrapper = await panel(); expect(wrapper.get('.el-table__body-wrapper').text()).toContain('92233720368547758.07');
    vi.mocked(listPaymentOrders).mockResolvedValue({ items: [{ ...order, amountMinor: '9223372036854775808' }], page: 1, pageSize: 20, total: 1 });
    wrapper.getComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh'); await flushPromises();
    expect(wrapper.get('.el-table__body-wrapper').text()).toContain('金额无法精确显示');
  });
});
