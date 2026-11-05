import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import TenantQuotaUsageDialog from './TenantQuotaUsageDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getTenantQuotaUsage, type TenantQuotaUsageSummaryResponse } from '../api/tenant-quota';
vi.mock('../api/tenant-quota', () => ({ getTenantQuotaUsage: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const summary = { tenantId: id(1), tenantIsActive: true, readAtUtc: '2026-11-05T00:00:00Z', metrics: [
  { id: id(2), metricCode: 'identity.seats', periodKey: 'default', limitValue: '100', usedValue: '2', reservedValue: '1' },
  { id: id(3), metricCode: 'files.storage_bytes', periodKey: 'default', limitValue: '9223372036854775807', usedValue: '9223372036854775707', reservedValue: '25' }] };
const dialog = { props: ['open'], emits: ['update:open'], template: '<section v-if="open"><slot/></section>' };
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; }
async function panel(permissions = ['tenancy.tenant_quota.read']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'reader', displayName: 'Reader', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, sessionId: id(10), permissions, preferredLocale: 'zh-CN', profileVersion: 1 };
  const wrapper = mount(TenantQuotaUsageDialog, { props: { open: true, tenantId: id(1), tenantName: 'Acme' }, global: { plugins: [pinia], stubs: { ArtFormDialog: dialog } } });
  await flushPromises(); return { wrapper, session };
}
describe('Host 配额用量读取', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(getTenantQuotaUsage).mockResolvedValue(summary); });
  it('只读权限足以展示精确额度、预留和余额', async () => {
    const { wrapper } = await panel(); const storage = wrapper.get('[data-testid="quota-metric-files.storage_bytes"]');
    expect(storage.get('[data-testid="quota-limit"]').text().replaceAll(',', '')).toBe('9223372036854775807');
    expect(storage.get('[data-testid="quota-remaining"]').text()).toBe('75'); expect(wrapper.get('[data-testid="quota-read-at"]').attributes('datetime')).toBe(summary.readAtUtc);
    expect(getTenantQuotaUsage).toHaveBeenCalledWith(id(1), expect.any(AbortSignal)); wrapper.unmount();
  });
  it('无读取权限不发请求', async () => { const { wrapper } = await panel([]); expect(getTenantQuotaUsage).not.toHaveBeenCalled(); wrapper.unmount(); });
  it('未配置与非活动状态不伪报零额度', async () => {
    vi.mocked(getTenantQuotaUsage).mockResolvedValue({ ...summary, tenantIsActive: false, metrics: [] }); const { wrapper } = await panel();
    expect(wrapper.text()).toContain('企业为非活动状态'); expect(wrapper.text()).toContain('当前未配置此项指标'); expect(wrapper.find('[data-testid="quota-limit"]').exists()).toBe(false); wrapper.unmount();
  });
  it('读取失败保留上次成功数据和时间', async () => {
    const { wrapper } = await panel(); vi.mocked(getTenantQuotaUsage).mockRejectedValue(new Error('offline'));
    await wrapper.get('[data-testid="quota-usage-refresh"]').trigger('click'); await flushPromises();
    expect(wrapper.text()).toContain('上次成功读取的数据'); expect(wrapper.get('[data-testid="quota-read-at"]').attributes('datetime')).toBe(summary.readAtUtc); wrapper.unmount();
  });
  it('服务端明确拒绝后清除原数据', async () => {
    const { wrapper } = await panel(); vi.mocked(getTenantQuotaUsage).mockRejectedValue({ status: 403, code: 'authorization.permission_denied' });
    await wrapper.get('[data-testid="quota-usage-refresh"]').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('无权读取');
    expect(wrapper.find('[data-testid="quota-limit"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each(['tenant', 'time', 'integer'] as const)('不接入 %s 无法核对的用量响应', async field => {
    vi.mocked(getTenantQuotaUsage).mockResolvedValue(field === 'tenant' ? { ...summary, tenantId: id(8) } : field === 'time' ? { ...summary, readAtUtc: 'not-a-date' }
      : { ...summary, metrics: [{ ...summary.metrics[0]!, limitValue: '9223372036854775808' }] });
    const { wrapper } = await panel(); expect(wrapper.text()).toContain('无法与原企业'); expect(wrapper.find('[data-testid="quota-limit"]').exists()).toBe(false); wrapper.unmount();
  });
  it('切换企业取消旧请求并保留新企业结果', async () => {
    const pending = deferred<TenantQuotaUsageSummaryResponse>(); vi.mocked(getTenantQuotaUsage).mockReturnValueOnce(pending.promise).mockResolvedValueOnce({ ...summary, tenantId: id(8), metrics: [] });
    const { wrapper } = await panel(); const signal = vi.mocked(getTenantQuotaUsage).mock.calls[0]![1];
    await wrapper.setProps({ tenantId: id(8), tenantName: 'Other' }); await flushPromises(); expect(signal?.aborted).toBe(true);
    pending.resolve(summary); await flushPromises(); expect(wrapper.get('[data-testid="quota-usage-target"]').text()).toBe('Other');
    expect(wrapper.find('[data-testid="quota-limit"]').exists()).toBe(false); wrapper.unmount();
  });
  it('撤权取消读取并丢弃迟到数据', async () => {
    const pending = deferred<TenantQuotaUsageSummaryResponse>(); vi.mocked(getTenantQuotaUsage).mockReturnValue(pending.promise);
    const { wrapper, session } = await panel(); const signal = vi.mocked(getTenantQuotaUsage).mock.calls[0]![1]; session.currentUser!.permissions = []; await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve(summary); await flushPromises(); expect(wrapper.find('[data-testid="quota-limit"]').exists()).toBe(false); wrapper.unmount();
  });
});
