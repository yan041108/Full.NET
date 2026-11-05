import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElInput, ElInputNumber } from 'element-plus';
import type { TenantPackageOfferResponse } from '@fullnet/client-contracts';
import PackageOfferEditor from './PackageOfferEditor.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getPackageOffer, listBillingFeatures, savePackageOffer } from '../api/subscription-billing';

vi.mock('../api/subscription-billing', () => ({ getPackageOffer: vi.fn(), listBillingFeatures: vi.fn(), savePackageOffer: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const offer = { packageId: id(1), paymentTenantId: id(2), merchantConfigId: id(3), amountMinor: 12500, currency: 'CNY', seatLimit: 10,
  storageLimit: 1000, durationDays: 30, entitlementIds: [id(4)], isActive: true, version: 1 };
const feature = { id: id(4), code: 'files', name: 'Files', description: null, entitlementType: 'Feature', isActive: true, version: 1 };
const dialog = { props: ['open', 'showConfirm', 'confirmDisabled'], emits: ['confirm', 'update:open'], template: '<section v-if="open"><slot/><button v-if="showConfirm !== false" data-testid="confirm" :disabled="confirmDisabled" @click="$emit(\'confirm\')">Confirm</button><button data-testid="close" @click="$emit(\'update:open\', false)">Close</button></section>' };
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
async function editor() {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'editor', displayName: 'Editor', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, sessionId: id(10), permissions: ['tenancy.tenant_packages.read', 'tenancy.tenant_packages.update', 'tenancy.tenant_entitlements.read', 'tenancy.tenant_entitlements.manage_bindings'], preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(PackageOfferEditor, { props: { packageId: offer.packageId }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  await wrapper.get('[data-testid="package-offer-edit"]').trigger('click'); await flushPromises(); return { wrapper, session };
}
describe('付费报价编辑及保存接续', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(getPackageOffer).mockResolvedValue(offer);
    vi.mocked(listBillingFeatures).mockResolvedValue([feature]); vi.mocked(savePackageOffer).mockImplementation(async (packageId, body) => ({ ...body, packageId, version: body.version + 1 })); });
  it('读取失败不能作为空报价覆盖，重新读取成功后才启用保存', async () => {
    vi.mocked(getPackageOffer).mockRejectedValueOnce({ status: 503, code: 'tenancy.package_offer.not_found' }).mockResolvedValueOnce(offer);
    const { wrapper } = await editor(); expect(wrapper.get('[data-testid="confirm"]').attributes('disabled')).toBeDefined(); expect(savePackageOffer).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="package-offer-reload"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="confirm"]').attributes('disabled')).toBeUndefined(); expect(wrapper.findComponent(ElInputNumber).props('modelValue')).toBe(12500); wrapper.unmount();
  });
  it('功能目录失败时不能以不完整目录保存报价', async () => {
    vi.mocked(listBillingFeatures).mockRejectedValue(new Error('catalog')); const { wrapper } = await editor();
    expect(wrapper.get('[data-testid="confirm"]').attributes('disabled')).toBeDefined(); expect(savePackageOffer).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('正式 404 后明确首次创建，正文不泄漏套餐响应字段', async () => {
    vi.mocked(getPackageOffer).mockRejectedValue({ status: 404, code: 'tenancy.package_offer.not_found' }); const { wrapper } = await editor();
    const inputs = wrapper.findAllComponents(ElInput); inputs[0]!.vm.$emit('update:modelValue', id(2)); inputs[1]!.vm.$emit('update:modelValue', id(3)); await flushPromises();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const body = vi.mocked(savePackageOffer).mock.calls[0]![1]; expect(body.version).toBe(0); expect(body).not.toHaveProperty('packageId'); expect(wrapper.text()).toContain('报价已保存'); wrapper.unmount();
  });
  it('成功保存冻结原版本且不提供重复提交', async () => {
    const { wrapper } = await editor(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const [target, body, signal] = vi.mocked(savePackageOffer).mock.calls[0]!;
    expect(target).toBe(offer.packageId); expect(body.version).toBe(1); expect(Object.isFrozen(body)).toBe(true); expect(signal).toBeInstanceOf(AbortSignal);
    expect(wrapper.text()).toContain('报价已保存'); expect(wrapper.find('[data-testid="confirm"]').exists()).toBe(false); expect(savePackageOffer).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('保存未知后关闭重开保留草稿，仅核对当前一致内容而不自动重发', async () => {
    vi.mocked(savePackageOffer).mockRejectedValue(new Error('lost')); const { wrapper } = await editor();
    wrapper.findComponent(ElInputNumber).vm.$emit('update:modelValue', 25000); await flushPromises(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    await wrapper.get('[data-testid="close"]').trigger('click'); await wrapper.get('[data-testid="package-offer-edit"]').trigger('click');
    expect(savePackageOffer).toHaveBeenCalledTimes(1); expect(getPackageOffer).toHaveBeenCalledTimes(1); expect(wrapper.text()).toContain('25000');
    vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, amountMinor: 25000, version: 2 });
    await wrapper.get('[data-testid="package-offer-reload"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="package-offer-current-state"]').text()).toContain('当前报价与提交内容一致'); expect(savePackageOffer).toHaveBeenCalledTimes(1);
    expect(wrapper.get('[data-testid="package-offer-receipt"]').text()).toContain('保存结果未确认'); wrapper.unmount();
  });
  it('冲突时逐项比较，保留草稿并采用新版本后仍需再次确认', async () => {
    vi.mocked(savePackageOffer).mockRejectedValueOnce({ status: 409, code: 'tenancy.package_offer.version_conflict' }).mockImplementationOnce(async (packageId, body) => ({ ...body, packageId, version: body.version + 1 }));
    const { wrapper } = await editor(); wrapper.findComponent(ElInputNumber).vm.$emit('update:modelValue', 25000); await flushPromises();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises(); const original = vi.mocked(savePackageOffer).mock.calls[0]![1];
    vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, amountMinor: 13000, seatLimit: 15, version: 5 });
    await wrapper.get('[data-testid="package-offer-reload"]').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('25000'); expect(wrapper.text()).toContain('13000');
    await wrapper.get('[data-testid="package-offer-keep-draft"]').trigger('click'); expect(savePackageOffer).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    const changed = vi.mocked(savePackageOffer).mock.calls[1]![1]; expect(changed.version).toBe(5); expect(changed.amountMinor).toBe(25000); expect(changed.seatLimit).toBe(10);
    expect(original.version).toBe(1); expect(wrapper.text()).toContain('报价已保存'); wrapper.unmount();
  });
  it('采用当前报价会用当前字段和版本替换草稿', async () => {
    vi.mocked(savePackageOffer).mockRejectedValue(new Error('lost')); const { wrapper } = await editor();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises(); vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, amountMinor: 18000, version: 4 });
    await wrapper.get('[data-testid="package-offer-reload"]').trigger('click'); await flushPromises(); await wrapper.get('[data-testid="package-offer-adopt-current"]').trigger('click'); await flushPromises();
    expect(wrapper.findComponent(ElInputNumber).props('modelValue')).toBe(18000); expect(savePackageOffer).toHaveBeenCalledTimes(1);
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises(); expect(vi.mocked(savePackageOffer).mock.calls[1]![1].version).toBe(4); wrapper.unmount();
  });
  it('未知后读取再次失败不能采用零版本或重新保存', async () => {
    vi.mocked(savePackageOffer).mockRejectedValue(new Error('lost')); const { wrapper } = await editor(); await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises();
    vi.mocked(getPackageOffer).mockRejectedValue(new Error('unavailable')); await wrapper.get('[data-testid="package-offer-reload"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="package-offer-adopt-current"]').exists()).toBe(false); expect(wrapper.find('[data-testid="confirm"]').exists()).toBe(false); expect(savePackageOffer).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('错配套餐报价不能接入原编辑目标', async () => {
    vi.mocked(getPackageOffer).mockResolvedValue({ ...offer, packageId: id(8) }); const { wrapper } = await editor();
    expect(wrapper.text()).toContain('响应与原目标不一致'); expect(wrapper.get('[data-testid="confirm"]').attributes('disabled')).toBeDefined(); wrapper.unmount();
  });
  it('保存响应字段错配时保留未知状态而不显示成功', async () => {
    vi.mocked(savePackageOffer).mockResolvedValue({ ...offer, version: 2, amountMinor: 1 }); const { wrapper } = await editor();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('响应与原目标不一致');
    expect(wrapper.get('[data-testid="package-offer-receipt"]').text()).toContain('保存结果未确认'); wrapper.unmount();
  });
  it.each(['target', 'permissions', 'close'] as const)('保存等待中发生 %s 变化取消请求并丢弃迟到成功', async change => {
    const pending = deferred<TenantPackageOfferResponse>(); vi.mocked(savePackageOffer).mockReturnValue(pending.promise); const { wrapper, session } = await editor();
    await wrapper.get('[data-testid="confirm"]').trigger('click'); await flushPromises(); const signal = vi.mocked(savePackageOffer).mock.calls[0]![2];
    expect(wrapper.get('[data-testid="package-offer-receipt"]').text()).toContain('正在保存报价');
    if (change === 'target') await wrapper.setProps({ packageId: id(8) }); else if (change === 'permissions') session.currentUser!.permissions = []; else await wrapper.get('[data-testid="close"]').trigger('click');
    await flushPromises(); expect(signal?.aborted).toBe(true); pending.resolve({ ...offer, version: 2 }); await flushPromises(); expect(wrapper.text()).not.toContain('报价已保存');
    if (change === 'close') { await wrapper.get('[data-testid="package-offer-edit"]').trigger('click'); expect(wrapper.text()).toContain('保存结果未确认'); expect(savePackageOffer).toHaveBeenCalledTimes(1); }
    wrapper.unmount();
  });
});
