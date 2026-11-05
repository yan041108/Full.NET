import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { TenantQuotaUsageDiagnosticResponse } from '@fullnet/client-contracts';
import TenantQuotaUsageDiagnostic from './TenantQuotaUsageDiagnostic.vue';
import { getTenantQuotaUsageDiagnostic } from '../api/tenant-quota';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
vi.mock('../api/tenant-quota', () => ({ getTenantQuotaUsageDiagnostic: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const value: TenantQuotaUsageDiagnosticResponse = { tenantId: id(1), metricCode: 'identity.seats', tenantIsActive: true, metricId: id(2),
  bookUsedValue: '9007199254741000', reservedValue: '0', authoritativeUsedValue: '9007199254740993', observedDifference: '-7', state: 'Different', readAtUtc: '2026-11-05T00:00:00Z' };
function setup(withDiagnostic = true) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(); session.state = 'authenticated';
  session.currentUser = { id: id(9), username: 'reader', displayName: 'Reader', tenantId: null, actorScope: 'host', scope: 'host', sessionId: id(10),
    isSuperAdministrator: false, passwordChangeRequired: false, preferredLocale: 'zh-CN', profileVersion: 1,
    permissions: ['tenancy.tenant_quota.read', ...(withDiagnostic ? ['tenancy.tenant_quota.reconcile_usage_baseline'] : [])] };
  const wrapper = mount(TenantQuotaUsageDiagnostic, { props: { tenantId: id(1) }, global: { plugins: [pinia] } });
  return { wrapper, session };
}
async function read(wrapper: ReturnType<typeof setup>['wrapper']) { await wrapper.get('[data-testid="quota-diagnostic-read"]').trigger('click'); await flushPromises(); }
describe('单企业权威用量诊断', () => {
  beforeEach(() => { vi.resetAllMocks(); useAdminI18n().setLocale('zh-CN'); vi.mocked(getTenantQuotaUsageDiagnostic).mockResolvedValue(value); });
  it('显式读取原企业和指标，大整数与负差额保持精确', async () => {
    const { wrapper } = setup(); expect(getTenantQuotaUsageDiagnostic).not.toHaveBeenCalled(); await read(wrapper);
    expect(getTenantQuotaUsageDiagnostic).toHaveBeenCalledWith(id(1), 'identity.seats', expect.any(AbortSignal));
    expect(wrapper.get('[data-testid="diagnostic-authoritative"]').text()).toBe('9007199254740993'); expect(wrapper.get('[data-testid="diagnostic-difference"]').text()).toBe('-7');
    expect(wrapper.get('[data-testid="quota-diagnostic-state"]').text()).toContain('观察存在差额'); wrapper.unmount();
  });
  it.each([
    { state: 'InProgress', observedDifference: null, reservedValue: '1' },
    { state: 'Unconfigured', observedDifference: null, metricId: null, bookUsedValue: null, reservedValue: null },
    { state: 'Unavailable', observedDifference: null, authoritativeUsedValue: null }
  ])('$state 不作差额或补零判断', async patch => {
    vi.mocked(getTenantQuotaUsageDiagnostic).mockResolvedValue({ ...value, ...patch }); const { wrapper } = setup(); await read(wrapper);
    expect(wrapper.get('[data-testid="diagnostic-difference"]').text()).toBe('暂不比较');
    if (patch.state === 'Unconfigured') expect(wrapper.get('[data-testid="diagnostic-book"]').text()).toBe('未知');
    if (patch.state === 'Unavailable') expect(wrapper.get('[data-testid="diagnostic-authoritative"]').text()).toBe('未知'); wrapper.unmount();
  });
  it.each([{ tenantId: id(99) }, { metricCode: 'files.storage_bytes' }])('响应身份错配不接入', async patch => {
    vi.mocked(getTenantQuotaUsageDiagnostic).mockResolvedValue({ ...value, ...patch }); const { wrapper } = setup(); await read(wrapper);
    expect(wrapper.find('[data-testid="quota-diagnostic-result"]').exists()).toBe(false); wrapper.unmount();
  });
  it.each([{ observedDifference: '-6' }, { state: 'Aligned' }, { reservedValue: '1' }, { authoritativeUsedValue: '9223372036854775808' }])('矛盾差额、预留或越界整数不显示为有效诊断', async patch => {
    vi.mocked(getTenantQuotaUsageDiagnostic).mockResolvedValue({ ...value, ...patch }); const { wrapper } = setup(); await read(wrapper);
    expect(wrapper.find('[data-testid="quota-diagnostic-result"]').exists()).toBe(false); wrapper.unmount();
  });
  it('切换指标取消旧请求且不自动读取新指标', async () => {
    const pending = Promise.withResolvers<TenantQuotaUsageDiagnosticResponse>(); vi.mocked(getTenantQuotaUsageDiagnostic).mockReturnValue(pending.promise);
    const { wrapper } = setup(); await read(wrapper); const signal = vi.mocked(getTenantQuotaUsageDiagnostic).mock.calls[0]![2];
    wrapper.getComponent({ name: 'ElRadioGroup' }).vm.$emit('update:modelValue', 'files.storage_bytes'); await flushPromises(); expect(signal?.aborted).toBe(true);
    pending.resolve(value); await flushPromises(); expect(wrapper.find('[data-testid="quota-diagnostic-result"]').exists()).toBe(false);
    expect(getTenantQuotaUsageDiagnostic).toHaveBeenCalledTimes(1); wrapper.unmount();
  });
  it('目标企业切换取消晚到结果', async () => {
    const pending = Promise.withResolvers<TenantQuotaUsageDiagnosticResponse>(); vi.mocked(getTenantQuotaUsageDiagnostic).mockReturnValue(pending.promise);
    const { wrapper } = setup(); await read(wrapper); const signal = vi.mocked(getTenantQuotaUsageDiagnostic).mock.calls[0]![2];
    await wrapper.setProps({ tenantId: id(3) }); expect(signal?.aborted).toBe(true); pending.resolve(value); await flushPromises();
    expect(wrapper.find('[data-testid="quota-diagnostic-result"]').exists()).toBe(false); wrapper.unmount();
  });
  it('撤权清除旧诊断并取消在途刷新', async () => {
    const { wrapper, session } = setup(); await read(wrapper); const pending = Promise.withResolvers<TenantQuotaUsageDiagnosticResponse>(); vi.mocked(getTenantQuotaUsageDiagnostic).mockReturnValue(pending.promise); await read(wrapper);
    const signal = vi.mocked(getTenantQuotaUsageDiagnostic).mock.calls[1]![2]; session.currentUser!.permissions = ['tenancy.tenant_quota.read']; await flushPromises();
    expect(signal?.aborted).toBe(true); pending.resolve(value); await flushPromises(); expect(wrapper.text()).toBe(''); wrapper.unmount();
  });
  it('临时失败保留带原时间的诊断', async () => {
    const { wrapper } = setup(); await read(wrapper); vi.mocked(getTenantQuotaUsageDiagnostic).mockRejectedValue(new Error('temporary')); await read(wrapper);
    expect(wrapper.get('[data-testid="quota-diagnostic-result"]').text()).toContain(value.readAtUtc); expect(wrapper.find('[data-testid="quota-diagnostic-failure"]').exists()).toBe(true); wrapper.unmount();
  });
  it.each([401, 403, 404])('明确拒绝 %s 清除旧结果', async status => {
    const { wrapper } = setup(); await read(wrapper); vi.mocked(getTenantQuotaUsageDiagnostic).mockRejectedValue({ status, code: 'test.denied', title: 'denied' }); await read(wrapper);
    expect(wrapper.find('[data-testid="quota-diagnostic-result"]').exists()).toBe(false); wrapper.unmount();
  });
  it('只有账面读取权限不展示诊断入口', () => { const { wrapper } = setup(false); expect(wrapper.text()).toBe(''); expect(getTenantQuotaUsageDiagnostic).not.toHaveBeenCalled(); wrapper.unmount(); });
});
