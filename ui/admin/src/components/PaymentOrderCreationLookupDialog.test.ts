import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentOrderCreationLookupResponse } from '@fullnet/client-contracts';
import PaymentOrderCreationLookupDialog from './PaymentOrderCreationLookupDialog.vue';
import { useSessionStore } from '../auth/session';
import { getPaymentOrderCreationRequest } from '../api/payment-orders';
vi.mock('../api/payment-orders', () => ({ getPaymentOrderCreationRequest: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const snapshot = (): PaymentOrderCreationLookupResponse => ({ idempotencyKey: id(1), readAtUtc: '2026-11-05T01:00:00Z', order: {
  id: id(4), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order', tradeStateKey: 'provider_unknown',
  amountMinor: '9223372036854775807', currency: 'CNY', subject: 'original', description: null, codeUrl: null,
  providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 7 } });
const dialog = { props: ['open'], template: '<section v-if="open"><slot/></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.orders.read'], fixedIdentity = false) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentOrderCreationLookupDialog, { props: { open: true, tenantId: id(2), idempotencyKey: id(1), fixedIdentity },
    global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function read(wrapper: VueWrapper) { await wrapper.get('form').trigger('submit'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getPaymentOrderCreationRequest).mockResolvedValue(snapshot()); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('原创建请求只读核对', () => {
  it('独立读取角色显式核对，保留精确金额并导航到原订单', async () => {
    const { wrapper } = await panel(); expect(getPaymentOrderCreationRequest).not.toHaveBeenCalled(); await read(wrapper);
    expect(getPaymentOrderCreationRequest).toHaveBeenCalledExactlyOnceWith(id(2), id(1), expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="creation-lookup-snapshot"]').text()).toContain('92233720368547758.07');
    await wrapper.get('[data-testid="creation-lookup-open-order"]').trigger('click');
    expect(wrapper.emitted('open-order')).toEqual([[id(4)]]);
  });
  it('固定身份打开只执行一次 GET', async () => {
    const { wrapper } = await panel(undefined, true); expect(getPaymentOrderCreationRequest).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="creation-lookup-key"]').attributes('readonly')).toBeDefined();
  });
  it('仅有创建权限不能核对', async () => { const { wrapper } = await panel(['payments.orders.create'], true);
    expect(getPaymentOrderCreationRequest).not.toHaveBeenCalled(); expect(wrapper.find('form').exists()).toBe(false); });
  it.each(['bad', '00000000-0000-0000-0000-000000000000'])('非法身份 %s 不发送请求', async value => {
    const { wrapper } = await panel(); await wrapper.get('[data-testid="creation-lookup-key"]').setValue(value); await read(wrapper);
    expect(getPaymentOrderCreationRequest).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="creation-lookup-problem"]').exists()).toBe(true);
  });
  it.each(['key', 'tenant', 'amount', 'version', 'time'])('拒绝 %s 错配回执', async field => {
    const original = snapshot(); const value = { ...original, order: { ...original.order } };
    if (field === 'key') value.idempotencyKey = id(6); else if (field === 'tenant') value.order.tenantId = id(6);
    else if (field === 'amount') value.order.amountMinor = '9223372036854775808'; else if (field === 'version') value.order.version = 0; else value.readAtUtc = 'invalid';
    vi.mocked(getPaymentOrderCreationRequest).mockResolvedValue(value); const { wrapper } = await panel(); await read(wrapper);
    expect(wrapper.emitted('found')).toBeUndefined(); expect(wrapper.find('[data-testid="creation-lookup-snapshot"]').exists()).toBe(false);
  });
  it('刷新失败清除旧快照并保留原身份', async () => {
    const { wrapper } = await panel(); await read(wrapper); vi.mocked(getPaymentOrderCreationRequest).mockRejectedValue({ type: 'about:blank', status: 404, code: 'payments.order.creation_request_not_found', title: 'not found' });
    await read(wrapper); expect(wrapper.find('[data-testid="creation-lookup-snapshot"]').exists()).toBe(false);
    expect((wrapper.get('[data-testid="creation-lookup-key"]').element as HTMLInputElement).value).toBe(id(1));
  });
  it.each(['identity', 'session', 'tenant', 'permission'])('%s 改变取消在途读取，迟到结果不能显示', async change => {
    let resolve!: (value: PaymentOrderCreationLookupResponse) => void;
    vi.mocked(getPaymentOrderCreationRequest).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await read(wrapper); const signal = vi.mocked(getPaymentOrderCreationRequest).mock.calls[0]![2]!;
    if (change === 'identity') await wrapper.get('[data-testid="creation-lookup-key"]').setValue(id(6));
    else if (change === 'session') session.currentUser!.sessionId = id(6); else if (change === 'tenant') session.currentUser!.tenantId = id(6);
    else session.currentUser!.permissions = ['payments.orders.create'];
    await flushPromises(); expect(signal.aborted).toBe(true); resolve(snapshot()); await flushPromises();
    expect(wrapper.emitted('found')).toBeUndefined(); expect(wrapper.find('[data-testid="creation-lookup-snapshot"]').exists()).toBe(false);
  });
});
