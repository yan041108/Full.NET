import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PaymentMerchantConfig, PaymentMerchantConfigListItem, PaymentMerchantConfigPage } from '@fullnet/client-contracts';
import PaymentMerchantConfigsView from './PaymentMerchantConfigsView.vue';
import { useSessionStore } from '../auth/session';
import { createPaymentMerchantConfig, disablePaymentMerchantConfig, getPaymentMerchantConfig, listPaymentMerchantConfigs } from '../api/payment-merchant-configs';
vi.mock('../api/payment-merchant-configs', () => ({ createPaymentMerchantConfig: vi.fn(), updatePaymentMerchantConfig: vi.fn(), disablePaymentMerchantConfig: vi.fn(), getPaymentMerchantConfig: vi.fn(), listPaymentMerchantConfigs: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const row: PaymentMerchantConfigListItem = { id: id(1), tenantId: id(2), name: 'Original merchant', channelKey: 'wechat_native',
  maskedAppId: 'app***', maskedMerchantId: 'merchant***', maskedCertificateSerialNo: 'cert***', maskedNotifyUrl: 'https://masked/***', maskedReturnUrl: '-',
  hasApiV3Key: true, hasPrivateKey: true, isDefault: false, isEnabled: true, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, version: 7 };
const detail: PaymentMerchantConfig = { ...row, appId: 'actual-app', merchantId: 'actual-merchant', certificateSerialNo: 'actual-cert', notifyUrl: 'https://example.com/notify', returnUrl: '' };
const dialog = { props: ['open', 'confirmTestId', 'confirmDisabled', 'saving'], emits: ['confirm', 'update:open'],
  template: '<section v-if="open"><slot/><button data-testid="merchant-test-close" @click="$emit(\'update:open\', false)">Close</button><button :data-testid="confirmTestId" :disabled="confirmDisabled || saving" @click="$emit(\'confirm\')">Confirm</button></section>' };
const wrappers: VueWrapper[] = [];
async function panel(permissions = ['payments.merchants.read', 'payments.merchants.create', 'payments.merchants.update']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(8), tenantId: null, actorScope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(PaymentMerchantConfigsView, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } }); wrappers.push(wrapper);
  await flushPromises(); return { wrapper, session };
}
const table = (wrapper: VueWrapper) => wrapper.get('.el-table__body-wrapper');
async function search(wrapper: VueWrapper) { await wrapper.get('form.merchant-filters').trigger('submit'); await flushPromises(); }
beforeEach(() => { vi.resetAllMocks(); vi.mocked(listPaymentMerchantConfigs).mockImplementation(async query => ({
  items: [{ ...row, tenantId: query?.tenantId ?? row.tenantId, channelKey: query?.channelKey ?? row.channelKey, isEnabled: query?.isEnabled ?? true }],
  page: query?.page ?? 1, pageSize: query?.pageSize ?? 20, total: 21 }));
  vi.mocked(getPaymentMerchantConfig).mockResolvedValue(detail); vi.mocked(disablePaymentMerchantConfig).mockResolvedValue({ ...detail, isEnabled: false, version: 8 }); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('商户配置运营筛选与观察作用域', () => {
  it('提交四条件，草稿不影响分页和刷新，重置回第一页', async () => {
    const { wrapper } = await panel(); await wrapper.get('[data-testid="merchant-filter-tenant"]').setValue(` ${id(2).toUpperCase()} `);
    const selects = wrapper.findAllComponents({ name: 'ElSelect' }); selects[0]!.vm.$emit('update:modelValue', 'alipay_page'); selects[1]!.vm.$emit('update:modelValue', 'disabled');
    await wrapper.get('[data-testid="merchant-filter-name"]').setValue(' merchant '); await search(wrapper);
    expect(vi.mocked(listPaymentMerchantConfigs).mock.lastCall![0]).toEqual({ page: 1, pageSize: 20, tenantId: id(2), channelKey: 'alipay_page', nameContains: 'merchant', isEnabled: false });
    await wrapper.get('[data-testid="merchant-filter-name"]').setValue('unsent'); wrapper.getComponent({ name: 'ElPagination' }).vm.$emit('current-change', 2); await flushPromises();
    expect(vi.mocked(listPaymentMerchantConfigs).mock.lastCall![0]).toMatchObject({ page: 2, nameContains: 'merchant', isEnabled: false });
    wrapper.getComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh'); await flushPromises(); expect(vi.mocked(listPaymentMerchantConfigs).mock.lastCall![0]?.page).toBe(2);
    await wrapper.get('[data-testid="merchant-filter-reset"]').trigger('click'); await flushPromises();
    expect(vi.mocked(listPaymentMerchantConfigs).mock.lastCall![0]).toMatchObject({ page: 1, tenantId: undefined, channelKey: undefined, nameContains: undefined, isEnabled: undefined });
  });
  it.each(['bad', '00000000-0000-0000-0000-000000000000', 'long-name', 'channel', 'enabled'])('非法条件 %s 不请求且刷新不回退到全部', async value => {
    const { wrapper } = await panel(); if (value === 'long-name') await wrapper.get('[data-testid="merchant-filter-name"]').setValue('A'.repeat(129));
    else if (value === 'channel' || value === 'enabled') wrapper.findAllComponents({ name: 'ElSelect' })[value === 'channel' ? 0 : 1]!.vm.$emit('update:modelValue', 'unknown');
    else await wrapper.get('[data-testid="merchant-filter-tenant"]').setValue(value);
    await search(wrapper); wrapper.getComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh'); await flushPromises();
    expect(listPaymentMerchantConfigs).toHaveBeenCalledTimes(1); expect(table(wrapper).text()).not.toContain(row.name);
  });
  it.each(['tenant', 'channel', 'enabled', 'page'])('来源 %s 错配拒绝整页', async field => {
    const { wrapper } = await panel(); vi.mocked(listPaymentMerchantConfigs).mockResolvedValue({ items: [{ ...row,
      tenantId: field === 'tenant' ? id(6) : id(2), channelKey: field === 'channel' ? 'alipay_page' : 'wechat_native', isEnabled: field !== 'enabled' }],
      page: field === 'page' ? 2 : 1, pageSize: 20, total: 1 });
    await wrapper.get('[data-testid="merchant-filter-tenant"]').setValue(id(2)); const selects = wrapper.findAllComponents({ name: 'ElSelect' });
    selects[0]!.vm.$emit('update:modelValue', 'wechat_native'); selects[1]!.vm.$emit('update:modelValue', 'enabled'); await search(wrapper);
    expect(table(wrapper).text()).not.toContain(row.name); expect(wrapper.text()).toContain('商户配置响应与请求');
  });
  it('新查询取消旧读取，迟到旧结果不覆盖', async () => {
    const { wrapper } = await panel(); let resolve!: (value: PaymentMerchantConfigPage) => void;
    vi.mocked(listPaymentMerchantConfigs).mockReturnValueOnce(new Promise(done => { resolve = done; })); await search(wrapper);
    const signal = vi.mocked(listPaymentMerchantConfigs).mock.lastCall![1]!; await wrapper.get('[data-testid="merchant-filter-reset"]').trigger('click'); await flushPromises();
    expect(signal.aborted).toBe(true); resolve({ items: [{ ...row, name: 'late' }], page: 1, pageSize: 20, total: 1 }); await flushPromises(); expect(table(wrapper).text()).not.toContain('late');
  });
  it.each(['account', 'session', 'tenant', 'permission'])('%s 改变关闭编辑并清除明文草稿', async change => {
    const { wrapper, session } = await panel(); await table(wrapper).get(`[data-testid="merchant-edit-${row.id}"]`).trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="merchant-editor-secret"]').setValue('private-draft');
    if (change === 'account') session.currentUser!.id = id(6); else if (change === 'session') session.currentUser!.sessionId = id(6);
    else if (change === 'tenant') session.currentUser!.tenantId = id(6); else session.currentUser!.permissions = ['payments.merchants.read'];
    await flushPromises(); expect(wrapper.find('[data-testid="merchant-editor-secret"]').exists()).toBe(false);
    expect(wrapper.html()).not.toContain('private-draft'); expect(createPaymentMerchantConfig).not.toHaveBeenCalled();
  });
  it('旧会话编辑读取被取消，迟到详情不能打开窗口', async () => {
    let resolve!: (value: PaymentMerchantConfig) => void; vi.mocked(getPaymentMerchantConfig).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await table(wrapper).get(`[data-testid="merchant-edit-${row.id}"]`).trigger('click');
    const signal = vi.mocked(getPaymentMerchantConfig).mock.lastCall![1]!; session.currentUser!.sessionId = id(6); await flushPromises();
    expect(signal.aborted).toBe(true); resolve(detail); await flushPromises(); expect(wrapper.find('[data-testid="merchant-editor-name"]').exists()).toBe(false);
  });
  it('只读角色只有脱敏列表，没有编辑/停用或创建', async () => {
    const { wrapper } = await panel(['payments.merchants.read']); expect(table(wrapper).text()).toContain(row.maskedMerchantId);
    expect(wrapper.find('[data-testid="payment-merchant-config-create"]').exists()).toBe(false);
    expect(table(wrapper).find(`[data-testid="merchant-edit-${row.id}"]`).exists()).toBe(false); expect(getPaymentMerchantConfig).not.toHaveBeenCalled();
  });
  it('停用先确认原身份，关闭零 POST，确认才调用一次', async () => {
    const { wrapper } = await panel(); const button = () => table(wrapper).get(`[data-testid="merchant-disable-${row.id}"]`);
    await button().trigger('click'); await flushPromises(); expect(wrapper.get('[data-testid="merchant-disable-identity"]').text()).toContain(row.tenantId);
    expect(disablePaymentMerchantConfig).not.toHaveBeenCalled(); await wrapper.get('[data-testid="merchant-test-close"]').trigger('click'); await button().trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="merchant-disable-confirm"]').trigger('click'); await flushPromises(); expect(disablePaymentMerchantConfig).toHaveBeenCalledExactlyOnceWith(row.id, expect.any(AbortSignal));
  });
  it('停用确认后原行版本变化不能继续提交', async () => {
    const { wrapper } = await panel(); await table(wrapper).get(`[data-testid="merchant-disable-${row.id}"]`).trigger('click'); await flushPromises();
    vi.mocked(listPaymentMerchantConfigs).mockResolvedValue({ items: [{ ...row, version: 8 }], page: 1, pageSize: 20, total: 1 });
    wrapper.getComponent({ name: 'ArtTableHeader' }).vm.$emit('refresh'); await flushPromises(); await wrapper.get('[data-testid="merchant-disable-confirm"]').trigger('click'); await flushPromises();
    expect(disablePaymentMerchantConfig).not.toHaveBeenCalled(); expect(wrapper.text()).toContain('原商户配置已变化');
  });
  it('发送创建后会话变化取消观察，迟到成功不刷新或再开编辑', async () => {
    let resolve!: (value: PaymentMerchantConfig) => void; vi.mocked(createPaymentMerchantConfig).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = await panel(); await wrapper.get('[data-testid="payment-merchant-config-create"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="merchant-editor-confirm"]').trigger('click'); await flushPromises(); const signal = vi.mocked(createPaymentMerchantConfig).mock.lastCall![1]!;
    session.currentUser!.permissions = []; await flushPromises(); expect(signal.aborted).toBe(true); resolve(detail); await flushPromises();
    expect(listPaymentMerchantConfigs).toHaveBeenCalledTimes(1); expect(wrapper.find('[data-testid="merchant-editor-name"]').exists()).toBe(false);
  });
});
