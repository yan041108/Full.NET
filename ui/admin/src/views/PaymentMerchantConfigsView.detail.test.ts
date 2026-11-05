import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { PaymentMerchantConfig, PaymentMerchantConfigListItem } from '@fullnet/client-contracts';
import PaymentMerchantConfigsView from './PaymentMerchantConfigsView.vue';
import { useSessionStore } from '../auth/session';
import { createPaymentMerchantConfig, disablePaymentMerchantConfig, getPaymentMerchantConfig, listPaymentMerchantConfigs, updatePaymentMerchantConfig } from '../api/payment-merchant-configs';
vi.mock('../api/payment-merchant-configs', () => ({ createPaymentMerchantConfig: vi.fn(), updatePaymentMerchantConfig: vi.fn(), disablePaymentMerchantConfig: vi.fn(), getPaymentMerchantConfig: vi.fn(), listPaymentMerchantConfigs: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const row: PaymentMerchantConfigListItem = { id: id(1), tenantId: id(2), name: 'Original merchant', channelKey: 'wechat_native', maskedAppId: 'app***',
  maskedMerchantId: 'merchant***', maskedCertificateSerialNo: 'cert***', maskedNotifyUrl: 'https://masked/***', maskedReturnUrl: '', hasApiV3Key: true,
  hasPrivateKey: true, isDefault: false, isEnabled: true, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, version: 7 };
const detail: PaymentMerchantConfig = { ...row, appId: 'actual-app', merchantId: 'actual-merchant', certificateSerialNo: 'actual-cert', notifyUrl: 'https://example.com/notify', returnUrl: '' };
const dialog = { props: ['open'], template: '<section v-if="open"><slot/></section>' }; const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.merchants.read']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentMerchantConfigsView, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } }); wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function lookup(wrapper: VueWrapper, value: string) { await wrapper.get('[data-testid="merchant-exact-id"]').setValue(value); await wrapper.get('form.merchant-lookup').trigger('submit'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getPaymentMerchantConfig).mockResolvedValue(detail);
  vi.mocked(listPaymentMerchantConfigs).mockImplementation(async query => ({ items: [query?.page === 2 ? { ...row, id: id(4), name: 'Page two' } : row], page: query?.page ?? 1, pageSize: 20, total: 40 })); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
it('空列表也能按 UUID 定位，非法与零 ID 不请求，只有 GET', async () => {
  vi.mocked(listPaymentMerchantConfigs).mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }); const { wrapper } = await panel();
  await lookup(wrapper, 'bad'); await lookup(wrapper, '00000000-0000-0000-0000-000000000000'); expect(getPaymentMerchantConfig).not.toHaveBeenCalled();
  await lookup(wrapper, ` ${id(1).toUpperCase()} `); expect(getPaymentMerchantConfig).toHaveBeenCalledExactlyOnceWith(id(1).toUpperCase(), expect.any(AbortSignal));
  expect(wrapper.get('[data-testid="merchant-detail-snapshot"]').text()).toContain('actual-merchant');
  expect(createPaymentMerchantConfig).not.toHaveBeenCalled(); expect(updatePaymentMerchantConfig).not.toHaveBeenCalled(); expect(disablePaymentMerchantConfig).not.toHaveBeenCalled();
});
it('只读行详情固定归属，错配不展示，也不打开编辑', async () => {
  vi.mocked(getPaymentMerchantConfig).mockResolvedValue({ ...detail, tenantId: id(6) }); const { wrapper } = await panel();
  await wrapper.get('.el-table__body-wrapper').get(`[data-testid="merchant-detail-${row.id}"]`).trigger('click'); await flushPromises();
  expect(wrapper.find('[data-testid="merchant-detail-snapshot"]').exists()).toBe(false); expect(wrapper.find('[data-testid="merchant-editor-name"]').exists()).toBe(false);
  expect(wrapper.get('[data-testid="merchant-detail-id"]').text()).toBe(row.id);
});
it('列表换页不替换已定位的独立原配置', async () => {
  const { wrapper } = await panel(); await lookup(wrapper, row.id);
  wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 2); await flushPromises();
  expect(wrapper.get('.el-table__body-wrapper').text()).toContain('Page two'); expect(wrapper.get('[data-testid="merchant-detail-id"]').text()).toBe(row.id);
  expect(getPaymentMerchantConfig).toHaveBeenCalledTimes(1);
});
it('上下文变化清空精确 ID 输入并取消详情', async () => {
  let resolve!: (value: PaymentMerchantConfig) => void; vi.mocked(getPaymentMerchantConfig).mockReturnValue(new Promise(done => { resolve = done; }));
  const { wrapper, session } = await panel(); await lookup(wrapper, row.id); const signal = vi.mocked(getPaymentMerchantConfig).mock.lastCall![1]!;
  session.currentUser!.sessionId = id(6); await flushPromises(); expect(signal.aborted).toBe(true);
  expect((wrapper.get('[data-testid="merchant-exact-id"]').element as HTMLInputElement).value).toBe(''); resolve(detail); await flushPromises();
  expect(wrapper.find('[data-testid="merchant-detail-snapshot"]').exists()).toBe(false);
});
