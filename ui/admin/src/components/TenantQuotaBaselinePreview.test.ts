import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElRadioGroup } from 'element-plus';
import TenantQuotaBaselinePreview from './TenantQuotaBaselinePreview.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { previewTenantQuotaUsage } from '../api/tenant-quota';
vi.mock('../api/tenant-quota', () => ({ previewTenantQuotaUsage: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const permission = 'tenancy.tenant_quota.reconcile_usage_baseline';
const batch = { dryRun: true, candidateCount: 1, appliedCount: 0, tenantIds: [id(1)], isComplete: false, nextAfterTenantId: id(2) };
const dialog = { props: ['open'], emits: ['update:open'], template: '<section v-if="open"><button data-testid="close" @click="$emit(\'update:open\', false)">close</button><slot/></section>' };
async function panel(permissions = [permission], tenant = false) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'reader', displayName: 'Reader', tenantId: tenant ? id(8) : null, actorScope: tenant ? 'tenant' : 'host', scope: tenant ? 'tenant' : 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), permissions, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(TenantQuotaBaselinePreview, { global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  await flushPromises(); return { wrapper, session };
}
async function open(wrapper: Awaited<ReturnType<typeof panel>>['wrapper']) { await wrapper.get('[data-testid="quota-preview-open"]').trigger('click'); await flushPromises(); }
async function load(wrapper: Awaited<ReturnType<typeof panel>>['wrapper']) { await wrapper.get('[data-testid="quota-preview-load"]').trigger('click'); await flushPromises(); }
describe('全局配额有界预检', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(previewTenantQuotaUsage).mockResolvedValue(batch); });
  it('打开不发请求，显式读取下一批，到末尾不自动续扫', async () => {
    const { wrapper } = await panel(); await open(wrapper); expect(previewTenantQuotaUsage).not.toHaveBeenCalled(); await load(wrapper);
    expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('identity.seats', null, expect.any(AbortSignal));
    expect(previewTenantQuotaUsage).toHaveBeenCalledTimes(1);
    vi.mocked(previewTenantQuotaUsage).mockResolvedValue({ ...batch, isComplete: true, nextAfterTenantId: null }); await load(wrapper);
    expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('identity.seats', id(2), expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="quota-preview-load"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="quota-preview-reset"]').trigger('click'); await load(wrapper);
    expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('identity.seats', null, expect.any(AbortSignal)); wrapper.unmount();
  });
  it.each([{ permissions: [] }, { permissions: ['tenancy.tenant_quota.read'] }])('缺少精确预检权限 %j 无入口', async ({ permissions }) => {
    const { wrapper } = await panel(permissions); expect(wrapper.find('[data-testid="quota-preview-open"]').exists()).toBe(false); expect(previewTenantQuotaUsage).not.toHaveBeenCalled(); wrapper.unmount();
  });
  it('租户上下文即使声明权限也不显示入口', async () => { const { wrapper } = await panel([permission], true); expect(wrapper.find('[data-testid="quota-preview-open"]').exists()).toBe(false); wrapper.unmount(); });
  it('失败保留上批，重试使用同一游标', async () => {
    const { wrapper } = await panel(); await open(wrapper); await load(wrapper); vi.mocked(previewTenantQuotaUsage).mockRejectedValue(new Error('offline')); await load(wrapper);
    expect(wrapper.get('[data-testid="quota-preview-result"]').text()).toContain(id(1)); expect(wrapper.text()).toContain('上次成功');
    vi.mocked(previewTenantQuotaUsage).mockResolvedValue({ ...batch, isComplete: true, nextAfterTenantId: null }); await load(wrapper);
    expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('identity.seats', id(2), expect.any(AbortSignal)); wrapper.unmount();
  });
  it('服务拒绝清除旧企业样本', async () => {
    const { wrapper } = await panel(); await open(wrapper); await load(wrapper);
    vi.mocked(previewTenantQuotaUsage).mockRejectedValue({ status: 403, code: 'authorization.permission_denied' }); await load(wrapper);
    expect(wrapper.find('[data-testid="quota-preview-result"]').exists()).toBe(false); expect(wrapper.text()).toContain('无权'); wrapper.unmount();
  });
  it('无效批次不推进游标', async () => {
    const { wrapper } = await panel(); await open(wrapper); await load(wrapper);
    vi.mocked(previewTenantQuotaUsage).mockResolvedValue({ ...batch, dryRun: false }); await load(wrapper);
    expect(wrapper.text()).toContain('未推进游标'); expect(wrapper.get('[data-testid="quota-preview-result"]').text()).toContain('当前批次 1');
    await load(wrapper); expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('identity.seats', id(2), expect.any(AbortSignal)); wrapper.unmount();
  });
  it.each(['metric', 'close', 'permission', 'session'] as const)('%s 变化取消请求且不接入晚到样本', async mode => {
    const pending = Promise.withResolvers<typeof batch>(); vi.mocked(previewTenantQuotaUsage).mockReturnValue(pending.promise);
    const { wrapper, session } = await panel(); await open(wrapper); await wrapper.get('[data-testid="quota-preview-load"]').trigger('click');
    const signal = vi.mocked(previewTenantQuotaUsage).mock.calls[0]![2]!;
    if (mode === 'metric') wrapper.findComponent(ElRadioGroup).vm.$emit('update:modelValue', 'files.storage_bytes');
    else if (mode === 'close') await wrapper.get('[data-testid="close"]').trigger('click');
    else if (mode === 'permission') session.currentUser!.permissions = [];
    else session.currentUser!.sessionId = id(11);
    await flushPromises(); expect(signal.aborted).toBe(true); pending.resolve(batch); await flushPromises();
    expect(wrapper.find('[data-testid="quota-preview-result"]').exists()).toBe(false); expect(previewTenantQuotaUsage).toHaveBeenCalledTimes(1);
    if (mode === 'metric') { vi.mocked(previewTenantQuotaUsage).mockResolvedValue(batch); await load(wrapper); expect(previewTenantQuotaUsage).toHaveBeenLastCalledWith('files.storage_bytes', null, expect.any(AbortSignal)); }
    if (mode === 'close') { await open(wrapper); expect(previewTenantQuotaUsage).toHaveBeenCalledTimes(1); }
    wrapper.unmount();
  });
});
