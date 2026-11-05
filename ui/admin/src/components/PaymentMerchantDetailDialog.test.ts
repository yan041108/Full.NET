import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentMerchantConfig } from '@fullnet/client-contracts';
import PaymentMerchantDetailDialog from './PaymentMerchantDetailDialog.vue';
import { useSessionStore } from '../auth/session';
import { getPaymentMerchantConfig } from '../api/payment-merchant-configs';
vi.mock('../api/payment-merchant-configs', () => ({ getPaymentMerchantConfig: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const merchant: PaymentMerchantConfig = { id: id(1), tenantId: id(2), name: 'Current merchant', channelKey: 'wechat_native', appId: 'actual-app', merchantId: 'original-merchant',
  certificateSerialNo: 'certificate-serial', notifyUrl: 'https://example.com/notify', returnUrl: '', hasApiV3Key: true, hasPrivateKey: false,
  isDefault: false, isEnabled: true, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, version: 7 };
const dialog = { name: 'ArtFormDialog', props: ['open', 'showConfirm'], emits: ['update:open'], template: '<section v-if="open"><slot/><button data-testid="detail-test-close" @click="$emit(\'update:open\', false)">Close</button><button v-if="showConfirm">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.merchants.read'], merchantConfigId = merchant.id, reference?: { tenantId: string | null; channelKey: string; version: number }) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentMerchantDetailDialog, { props: { open: true, merchantConfigId, reference }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getPaymentMerchantConfig).mockResolvedValue({ ...merchant }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('商户配置独立只读详情', () => {
  it('只读角色精确 GET 一次，显示当前配置和密钥存在标志，地址不是链接', async () => {
    const { wrapper } = await panel(); expect(getPaymentMerchantConfig).toHaveBeenCalledExactlyOnceWith(merchant.id, expect.any(AbortSignal));
    const snapshot = wrapper.get('[data-testid="merchant-detail-snapshot"]'); expect(snapshot.text()).toContain(merchant.merchantId);
    expect(snapshot.text()).toContain('API v3 密钥已配置'); expect(snapshot.text()).toContain(merchant.notifyUrl);
    expect(wrapper.find('input,textarea,a').exists()).toBe(false); expect(wrapper.getComponent({ name: 'ArtFormDialog' }).props('showConfirm')).toBe(false);
  });
  it('Host 归属配置可独立读取', async () => { vi.mocked(getPaymentMerchantConfig).mockResolvedValue({ ...merchant, tenantId: null });
    const { wrapper } = await panel(); expect(wrapper.get('[data-testid="merchant-detail-snapshot"]').text()).toContain('Host'); });
  it.each(['id', 'tenant', 'channel', 'version', 'flag', 'date'])('来源 %s 错配清空，不展示其他配置', async field => {
    const value = { ...merchant }; if (field === 'id') value.id = id(6); else if (field === 'tenant') value.tenantId = id(6);
    else if (field === 'channel') value.channelKey = 'alipay_page'; else if (field === 'version') value.version = 6;
    else if (field === 'flag') (value as unknown as Record<string, unknown>).hasPrivateKey = 'true'; else value.createdAtUtc = 'invalid';
    vi.mocked(getPaymentMerchantConfig).mockResolvedValue(value); const { wrapper } = await panel(undefined, merchant.id, { tenantId: merchant.tenantId, channelKey: merchant.channelKey, version: 7 });
    expect(wrapper.find('[data-testid="merchant-detail-snapshot"]').exists()).toBe(false); expect(wrapper.find('[data-testid="merchant-detail-problem"]').exists()).toBe(true);
  });
  it.each(['bad', '00000000-0000-0000-0000-000000000000'])('非法配置 ID %s 不请求', async value => {
    const { wrapper } = await panel(undefined, value); expect(getPaymentMerchantConfig).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('请输入非零商户配置 UUID');
  });
  it.each([503, 403, 404])('刷新 %s 清空旧详情并保留原 ID', async status => {
    const { wrapper } = await panel(); vi.mocked(getPaymentMerchantConfig).mockRejectedValue({ type: 'about:blank', code: 'test.failed', status, title: 'unavailable' });
    await wrapper.get('[data-testid="merchant-detail-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="merchant-detail-snapshot"]').exists()).toBe(false); expect(wrapper.get('[data-testid="merchant-detail-id"]').text()).toBe(merchant.id);
  });
  it.each(['account', 'session', 'tenant', 'permission'])('%s 改变取消并丢弃迟到详情', async change => {
    let resolve!: (value: PaymentMerchantConfig) => void; vi.mocked(getPaymentMerchantConfig).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); const signal = vi.mocked(getPaymentMerchantConfig).mock.lastCall![1]!;
    if (change === 'account') session.currentUser!.id = id(6); else if (change === 'session') session.currentUser!.sessionId = id(6);
    else if (change === 'tenant') session.currentUser!.tenantId = id(6); else session.currentUser!.permissions = ['payments.merchants.create'];
    await flushPromises(); expect(signal.aborted).toBe(true); resolve(merchant); await flushPromises(); expect(wrapper.find('[data-testid="merchant-detail-snapshot"]').exists()).toBe(false);
  });
  it.each(['payments.merchants.create', 'payments.merchants.update'])('仅 %s 不能代替读取权限', async permission => {
    const { wrapper } = await panel([permission]); expect(getPaymentMerchantConfig).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="merchant-detail-id"]').exists()).toBe(false);
  });
  it('切换精确目标取消旧读取，迟到内容不能覆盖新配置', async () => {
    let resolve!: (value: PaymentMerchantConfig) => void; vi.mocked(getPaymentMerchantConfig).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    const { wrapper } = await panel(); const signal = vi.mocked(getPaymentMerchantConfig).mock.lastCall![1]!;
    vi.mocked(getPaymentMerchantConfig).mockResolvedValue({ ...merchant, id: id(6), name: 'New target' }); await wrapper.setProps({ merchantConfigId: id(6) }); await flushPromises();
    expect(signal.aborted).toBe(true); resolve(merchant); await flushPromises(); expect(wrapper.get('[data-testid="merchant-detail-snapshot"]').text()).toContain('New target');
  });
  it('关闭取消未完成请求', async () => {
    vi.mocked(getPaymentMerchantConfig).mockReturnValue(new Promise(() => {})); const { wrapper } = await panel(); const signal = vi.mocked(getPaymentMerchantConfig).mock.lastCall![1]!;
    await wrapper.get('[data-testid="detail-test-close"]').trigger('click'); expect(signal.aborted).toBe(true);
  });
  it('来源额外夹带密钥不进入可展示快照', async () => {
    vi.mocked(getPaymentMerchantConfig).mockResolvedValue({ ...merchant, privateKeyPem: 'DO-NOT-ECHO-PRIVATE', apiV3Key: 'DO-NOT-ECHO-API' } as PaymentMerchantConfig);
    const { wrapper } = await panel(); expect(wrapper.html()).not.toContain('DO-NOT-ECHO'); expect(wrapper.get('[data-testid="merchant-detail-snapshot"]').text()).toContain(merchant.name);
  });
});
