import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CreatePaymentOrderRequest, PaymentOrder } from '@fullnet/client-contracts';
import PaymentOrderCreateDialog from './PaymentOrderCreateDialog.vue';
import { useSessionStore } from '../auth/session';
import { createPaymentOrder, getPaymentOrderCreationRequest } from '../api/payment-orders';
vi.mock('../api/payment-orders', () => ({ createPaymentOrder: vi.fn(), getPaymentOrderCreationRequest: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const orderFor = (body: CreatePaymentOrderRequest): PaymentOrder => ({ id: id(5), tenantId: body.tenantId,
  merchantConfigId: body.merchantConfigId ?? id(3), channelKey: body.channelKey ?? 'alipay_page', outTradeNo: 'original-order',
  tradeStateKey: 'awaiting_payment', amountMinor: body.amountMinor, currency: body.currency, subject: body.subject, description: body.description ?? null,
  codeUrl: 'weixin://original', providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 2 });
const dialog = { props: ['open', 'confirmTestId', 'confirmDisabled', 'saving', 'showConfirm'], emits: ['confirm', 'update:open'],
  template: '<section v-if="open"><slot/><button data-testid="create-test-close" @click="$emit(\'update:open\', false)">Close</button><button v-if="showConfirm !== false" :data-testid="confirmTestId" :disabled="confirmDisabled || saving" @click="$emit(\'confirm\')">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.orders.create', 'payments.orders.read']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrderCreateDialog, { props: { open: true }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function fill(wrapper: VueWrapper) {
  await wrapper.get('[data-testid="payment-order-create-tenant"]').setValue(id(2));
  await wrapper.get('[data-testid="payment-order-create-subject"]').setValue(' original ');
  await wrapper.get('[data-testid="payment-order-create-amount"]').setValue('92233720368547758.07');
}
async function send(wrapper: VueWrapper) { await wrapper.get('[data-testid="payment-order-create-confirm"]').trigger('click'); await flushPromises(); }
async function reopen(wrapper: VueWrapper) { await wrapper.setProps({ open: false }); await wrapper.setProps({ open: true }); await flushPromises(); }
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(createPaymentOrder).mockImplementation(async body => orderFor(body));
  vi.mocked(getPaymentOrderCreationRequest).mockImplementation(async (_tenant, key) => ({ idempotencyKey: key,
    order: orderFor(vi.mocked(createPaymentOrder).mock.calls[0]![0]), readAtUtc: '2026-11-05T01:00:00Z' }));
});
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));

describe('支付创建原请求恢复', () => {
  it('未知后关闭重开锁定同一正文，显式恢复不换键也不改金额', async () => {
    vi.mocked(createPaymentOrder).mockRejectedValueOnce(new Error('response lost'));
    const { wrapper } = await panel(); await fill(wrapper); await send(wrapper);
    const original = vi.mocked(createPaymentOrder).mock.calls[0]![0];
    expect(original.amountMinor).toBe('9223372036854775807'); expect(original.subject).toBe('original');
    await wrapper.get('[data-testid="create-test-close"]').trigger('click'); await reopen(wrapper);
    expect(wrapper.get('[data-testid="payment-order-create-amount"]').attributes('readonly')).toBeDefined();
    expect(wrapper.get('[data-testid="payment-creation-original-key"]').text()).toContain(original.idempotencyKey);
    expect(createPaymentOrder).toHaveBeenCalledTimes(1); await send(wrapper);
    expect(vi.mocked(createPaymentOrder).mock.calls[1]![0]).toEqual(original);
    expect(wrapper.emitted('created')).toHaveLength(1);
  });
  it('原请求核对仅 GET，确认后重开仍锁定，明确另一订单才换键', async () => {
    vi.mocked(createPaymentOrder).mockRejectedValueOnce(new Error('lost'));
    const { wrapper } = await panel(); await fill(wrapper); await send(wrapper);
    const original = vi.mocked(createPaymentOrder).mock.calls[0]![0];
    await wrapper.get('[data-testid="payment-creation-lookup"]').trigger('click'); await flushPromises();
    expect(getPaymentOrderCreationRequest).toHaveBeenCalledExactlyOnceWith(original.tenantId, original.idempotencyKey, expect.any(AbortSignal));
    expect(createPaymentOrder).toHaveBeenCalledTimes(1); expect(wrapper.emitted('created')?.[0]?.[1]).toBe('2026-11-05T01:00:00Z');
    await reopen(wrapper); expect(wrapper.get('[data-testid="payment-order-create-confirm"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="payment-order-create-another"]').trigger('click'); await fill(wrapper); await send(wrapper);
    expect(vi.mocked(createPaymentOrder).mock.calls[1]![0].idempotencyKey).not.toBe(original.idempotencyKey);
  });
  it('未找到不会解锁或另建，缺少独立读取权限不显示核对入口', async () => {
    vi.mocked(createPaymentOrder).mockRejectedValue(new Error('lost'));
    const first = await panel(['payments.orders.create']); await fill(first.wrapper); await send(first.wrapper);
    expect(first.wrapper.find('[data-testid="payment-creation-lookup"]').exists()).toBe(false); expect(getPaymentOrderCreationRequest).not.toHaveBeenCalled();
    const second = await panel(); await fill(second.wrapper); await send(second.wrapper);
    vi.mocked(getPaymentOrderCreationRequest).mockRejectedValue({ type: 'about:blank', status: 404, code: 'payments.order.creation_request_not_found', title: 'not found' });
    await second.wrapper.get('[data-testid="payment-creation-lookup"]').trigger('click'); await flushPromises();
    await second.wrapper.get('[data-testid="create-test-close"]').trigger('click'); await flushPromises();
    expect(second.wrapper.get('[data-testid="payment-order-create-amount"]').attributes('readonly')).toBeDefined();
    expect(second.wrapper.find('[data-testid="payment-order-create-another"]').exists()).toBe(false);
    expect(createPaymentOrder).toHaveBeenCalledTimes(2);
  });
  it.each(['tenantId', 'merchantConfigId', 'amountMinor', 'currency', 'subject'])('错配回执 %s 不冒充成功', async field => {
    vi.mocked(createPaymentOrder).mockImplementation(async body => ({ ...orderFor(body), [field]: field.endsWith('Id') ? '00000000-0000-0000-0000-000000000000' : 'wrong' }));
    const { wrapper } = await panel(); await fill(wrapper); await send(wrapper);
    expect(wrapper.emitted('created')).toBeUndefined(); expect(wrapper.get('[data-testid="payment-creation-problem"]').text()).toContain('回执与原创建身份或参数不一致');
  });
  it('首个明确前置校验拒绝可修正；未知之后的同码失败仍保留原请求', async () => {
    const rejected = { type: 'about:blank', status: 400, code: 'payments.merchant_config.unavailable', title: 'unavailable' };
    vi.mocked(createPaymentOrder).mockRejectedValueOnce(rejected);
    const first = await panel(); await fill(first.wrapper); await send(first.wrapper);
    expect(first.wrapper.get('[data-testid="payment-order-create-amount"]').attributes('readonly')).toBeUndefined();
    vi.mocked(createPaymentOrder).mockRejectedValueOnce(new Error('lost')).mockRejectedValueOnce(rejected);
    const second = await panel(); await fill(second.wrapper); await send(second.wrapper); await send(second.wrapper);
    expect(second.wrapper.get('[data-testid="payment-order-create-amount"]').attributes('readonly')).toBeDefined();
  });
  it.each(['account', 'session', 'tenant', 'permission'])('%s 改变清空并取消，迟到成功不接入', async change => {
    let resolve!: (value: PaymentOrder) => void; vi.mocked(createPaymentOrder).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await fill(wrapper); await send(wrapper);
    const call = vi.mocked(createPaymentOrder).mock.calls[0]!;
    if (change === 'account') session.currentUser!.id = id(7);
    else if (change === 'session') session.currentUser!.sessionId = id(7);
    else if (change === 'tenant') session.currentUser!.tenantId = id(7);
    else session.currentUser!.permissions = ['payments.orders.read'];
    await flushPromises(); expect(call[1]!.aborted).toBe(true); resolve(orderFor(call[0])); await flushPromises();
    expect(wrapper.emitted('created')).toBeUndefined(); expect(wrapper.find('[data-testid="payment-creation-original-key"]').exists()).toBe(false);
  });
  it('发送中重复点击只调用一次', async () => {
    vi.mocked(createPaymentOrder).mockReturnValue(new Promise(() => {}));
    const { wrapper } = await panel(); await fill(wrapper); await send(wrapper); await send(wrapper); expect(createPaymentOrder).toHaveBeenCalledTimes(1);
  });
});
