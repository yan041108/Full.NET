import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { reactive } from 'vue';
import { createPinia, setActivePinia } from 'pinia';
import type { PaymentRefund, PaymentRefundPage } from '@fullnet/client-contracts';
import PaymentRefundsView from './PaymentRefundsView.vue';
import { useSessionStore } from '../auth/session';
import { getPaymentRefund, listPaymentRefunds } from '../api/payment-refunds';
const routing = vi.hoisted(() => ({ route: undefined as undefined | { query: Record<string, unknown> }, replace: vi.fn() }));
vi.mock('vue-router', async original => ({ ...await original<typeof import('vue-router')>(), useRoute: () => routing.route, useRouter: () => ({ replace: routing.replace }) }));
vi.mock('../api/payment-refunds', () => ({ getPaymentRefund: vi.fn(), listPaymentRefunds: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const row: PaymentRefund = { id: id(1), orderId: id(2), tenantId: id(3), merchantConfigId: id(4), outTradeNo: 'original-order',
  outRefundNo: 'original-refund', refundStateKey: 'provider_unknown', amountMinor: '29', currency: 'CNY', reason: 'original reason', providerRefundId: null,
  failMessage: 'unconfirmed', createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, completedAtUtc: null, version: 2 };
const page = (items: PaymentRefund[] = [row], current = 1, total = 41): PaymentRefundPage => ({ items, page: current, pageSize: 20, total });
const dialog = { props: ['open', 'showConfirm'], emits: ['update:open'], template: '<section v-if="open" data-testid="test-refund-dialog"><slot/><button data-testid="test-refund-close" @click="$emit(\'update:open\', false)">Close</button></section>' };
const wrappers: VueWrapper[] = [];
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function panel(permissions = ['payments.refunds.read'], tenantId: string | null = null) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(10), tenantId, actorScope: 'host', scope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentRefundsView, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
beforeEach(() => {
  vi.resetAllMocks(); routing.route = reactive({ query: {} });
  routing.replace.mockImplementation(async (target: { query: Record<string, unknown> }) => { routing.route!.query = target.query; });
  vi.mocked(listPaymentRefunds).mockImplementation(async query => page([{ ...row, orderId: query?.orderId ?? row.orderId,
    refundStateKey: query?.refundStateKey ?? row.refundStateKey }], query?.page ?? 1));
  vi.mocked(getPaymentRefund).mockResolvedValue(row);
});
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('原退款记录核对控制台', () => {
  it('深链接按原订单在来源分页，不自动读取详情', async () => {
    routing.route!.query = { orderId: row.orderId }; await panel();
    expect(listPaymentRefunds).toHaveBeenCalledWith({ page: 1, pageSize: 20, orderId: row.orderId, refundStateKey: undefined }, expect.any(AbortSignal));
    expect(getPaymentRefund).not.toHaveBeenCalled();
  });
  it('没有独立读取权限或切入租户时不装载全局记录', async () => {
    await panel([]); await panel(['payments.refunds.read'], id(3)); expect(listPaymentRefunds).not.toHaveBeenCalled(); expect(getPaymentRefund).not.toHaveBeenCalled();
  });
  it.each(['bad', '00000000-0000-0000-0000-000000000000', ['multiple'], null])('无效深链接 %s 不回退全部，显式重置才读取', async value => {
    routing.route!.query = { orderId: value }; const { wrapper } = await panel();
    expect(listPaymentRefunds).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('不会回退为全部');
    await wrapper.get('[data-testid="refunds-reset"]').trigger('click'); await flushPromises();
    expect(routing.replace).toHaveBeenCalledWith({ name: 'payment-refunds', query: {} }); expect(listPaymentRefunds).toHaveBeenCalledTimes(1);
  });
  it('筛选回首页；翻页只携带已提交条件，输入草稿不隐式改变页范围', async () => {
    const { wrapper } = await panel();
    wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 2); await flushPromises();
    await wrapper.get('[data-testid="refunds-filter-order"]').setValue(row.orderId);
    wrapper.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 'provider_unknown');
    await wrapper.get('[data-testid="refunds-search"]').trigger('click'); await flushPromises();
    expect(vi.mocked(listPaymentRefunds).mock.lastCall![0]).toMatchObject({ page: 1, orderId: row.orderId, refundStateKey: 'provider_unknown' });
    await wrapper.get('[data-testid="refunds-filter-order"]').setValue(id(20));
    wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 2); await flushPromises();
    expect(vi.mocked(listPaymentRefunds).mock.lastCall![0]).toMatchObject({ page: 2, orderId: row.orderId, refundStateKey: 'provider_unknown' });
    await wrapper.get('[data-testid="refunds-search"]').trigger('click'); await flushPromises();
    expect(vi.mocked(listPaymentRefunds).mock.lastCall![0]).toMatchObject({ page: 1, orderId: id(20) });
  });
  it('来源返回其他订单时整页拒绝，不用本地筛选掩盖错误', async () => {
    routing.route!.query = { orderId: id(20) }; vi.mocked(listPaymentRefunds).mockResolvedValue(page());
    const { wrapper } = await panel(); expect(wrapper.text()).toContain('退款响应与原查询条件或目标不一致');
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')).toEqual([]);
  });
  it('新筛选取消旧查询，迟到的旧结果不能覆盖新页', async () => {
    const old = deferred<PaymentRefundPage>(); vi.mocked(listPaymentRefunds).mockReturnValueOnce(old.promise);
    const { wrapper } = await panel(); const signal = vi.mocked(listPaymentRefunds).mock.calls[0]![1]!;
    await wrapper.get('[data-testid="refunds-filter-order"]').setValue(id(20)); await wrapper.get('[data-testid="refunds-search"]').trigger('click'); await flushPromises();
    expect(signal.aborted).toBe(true); old.resolve(page()); await flushPromises();
    expect(wrapper.getComponent({ name: 'ElTable' }).props('data')[0].orderId).toBe(id(20));
  });
  it('可按列表外原退款 ID 独立读取；刷新失败清空快照并保留请求身份', async () => {
    const { wrapper } = await panel(); vi.mocked(getPaymentRefund).mockResolvedValue({ ...row, id: id(20) });
    await wrapper.get('[data-testid="refunds-exact-id"]').setValue(id(20)); await wrapper.get('[data-testid="refunds-read-id"]').trigger('click'); await flushPromises();
    expect(getPaymentRefund).toHaveBeenCalledWith(id(20), expect.any(AbortSignal)); expect(wrapper.get('[data-testid="refund-detail-id"]').text()).toBe(id(20));
    expect(wrapper.get('[data-testid="refund-detail-snapshot"]').text()).toContain('original reason');
    expect(wrapper.find('[data-testid="refund-read-original-order"]').exists()).toBe(false);
    vi.mocked(getPaymentRefund).mockRejectedValue({ status: 503, code: 'test.unavailable', type: 'about:blank', title: 'unavailable' });
    await wrapper.get('[data-testid="refund-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="refund-detail-snapshot"]').exists()).toBe(false); expect(wrapper.get('[data-testid="refund-detail-id"]').text()).toBe(id(20));
    expect(listPaymentRefunds).toHaveBeenCalledTimes(1);
  });
  it('错 ID 的详情响应拒绝接入，完整 Int64 金额精确展示', async () => {
    const { wrapper } = await panel(); vi.mocked(getPaymentRefund).mockResolvedValue({ ...row, id: id(20) });
    await wrapper.get('[data-testid="refunds-exact-id"]').setValue(row.id); await wrapper.get('[data-testid="refunds-read-id"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="refund-detail-snapshot"]').exists()).toBe(false);
    vi.mocked(getPaymentRefund).mockResolvedValue({ ...row, amountMinor: '9223372036854775807' });
    await wrapper.get('[data-testid="refund-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="refund-detail-amount"]').text()).toBe('92233720368547758.07 CNY');
  });
  it('关闭旧详情取消请求，另一个目标不接受旧结果', async () => {
    const old = deferred<PaymentRefund>(); const { wrapper } = await panel(); vi.mocked(getPaymentRefund).mockReturnValueOnce(old.promise);
    await wrapper.get('[data-testid="refunds-exact-id"]').setValue(row.id); await wrapper.get('[data-testid="refunds-read-id"]').trigger('click');
    const signal = vi.mocked(getPaymentRefund).mock.calls[0]![1]!;
    await wrapper.get('[data-testid="test-refund-close"]').trigger('click');
    vi.mocked(getPaymentRefund).mockResolvedValue({ ...row, id: id(20), outRefundNo: 'new-target' });
    await wrapper.get('[data-testid="refunds-exact-id"]').setValue(id(20)); await wrapper.get('[data-testid="refunds-read-id"]').trigger('click'); await flushPromises();
    expect(signal.aborted).toBe(true); old.resolve(row); await flushPromises(); expect(wrapper.get('[data-testid="refund-detail-snapshot"]').text()).toContain('new-target');
  });
  it.each(['permission', 'tenant', 'session'])('%s 改变关闭详情并取消在途读取', async change => {
    const old = deferred<PaymentRefund>(); const { wrapper, session } = await panel(); vi.mocked(getPaymentRefund).mockReturnValue(old.promise);
    await wrapper.get('[data-testid="refunds-exact-id"]').setValue(row.id); await wrapper.get('[data-testid="refunds-read-id"]').trigger('click');
    const signal = vi.mocked(getPaymentRefund).mock.calls[0]![1]!;
    if (change === 'permission') session.currentUser!.permissions = []; else if (change === 'tenant') session.currentUser!.tenantId = id(20); else session.currentUser!.sessionId = id(20);
    await flushPromises(); expect(signal.aborted).toBe(true); old.resolve(row); await flushPromises();
    expect(wrapper.find('[data-testid="test-refund-dialog"]').exists()).toBe(false); expect(getPaymentRefund).toHaveBeenCalledTimes(1);
  });
});
