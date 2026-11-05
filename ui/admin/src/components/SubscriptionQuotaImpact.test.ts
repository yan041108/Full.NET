import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { TenantPackageOfferResponse } from '@fullnet/client-contracts';
import SubscriptionQuotaImpact from './SubscriptionQuotaImpact.vue';
import { useSessionStore } from '../auth/session';
import { getTenantQuotaUsage } from '../api/tenant-quota';
vi.mock('../api/tenant-quota', () => ({ getTenantQuotaUsage: vi.fn() }));
const id = (n: number) => `019bc2b1-2a40-7cc3-8992-a80de51bf2${String(n).padStart(2, '0')}`;
const offer = { packageId: id(2), version: 1, seatLimit: 1, storageLimit: 1000 } as TenantPackageOfferResponse;
const summary = { tenantId: id(1), tenantIsActive: true, readAtUtc: '2026-11-05T00:00:00Z', metrics: [
  { id: id(3), metricCode: 'identity.seats', periodKey: 'default', limitValue: '100', usedValue: '2', reservedValue: '1' },
  { id: id(4), metricCode: 'files.storage_bytes', periodKey: 'default', limitValue: '9223372036854775807', usedValue: '9007199254740993', reservedValue: '3' }] };
const wrappers: VueWrapper[] = [];
function panel(permissions = ['tenancy.tenant_quota.read']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia); session.state = 'authenticated';
  session.currentUser = { id: id(9), sessionId: id(10), tenantId: null, actorScope: 'host', scope: 'host', permissions } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(SubscriptionQuotaImpact, { props: { tenantId: id(1), offer }, global: { plugins: [pinia] } });
  wrappers.push(wrapper); return { wrapper, session };
}
beforeEach(() => { vi.resetAllMocks(); vi.mocked(getTenantQuotaUsage).mockResolvedValue(summary); });
afterEach(() => wrappers.splice(0).forEach(wrapper => wrapper.unmount()));
describe('新套餐当前用量影响', () => {
  it('不自动读取，显式读取按精确整数计算已用和预留的影响', async () => {
    const { wrapper } = panel(); await flushPromises(); expect(getTenantQuotaUsage).not.toHaveBeenCalled();
    await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    expect(getTenantQuotaUsage).toHaveBeenCalledWith(id(1), expect.any(AbortSignal));
    const storage = wrapper.get('[data-testid="subscription-quota-impact-files.storage_bytes"]');
    expect(storage.get('[data-testid="impact-used"]').text()).toBe('9007199254740993');
    expect(storage.get('[data-testid="impact-remaining"]').text()).toBe('-9007199254739996');
    expect(storage.get('[data-testid="impact-over-limit"]').text()).toContain('存量保留');
    expect(wrapper.get('[data-testid="subscription-quota-impact-read-at"]').attributes('datetime')).toBe(summary.readAtUtc);
  });
  it('未配置不能补零或宣称没有影响', async () => {
    vi.mocked(getTenantQuotaUsage).mockResolvedValue({ ...summary, metrics: [] });
    const { wrapper } = panel(); await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    expect(wrapper.findAll('[data-testid="impact-unknown"]')).toHaveLength(2);
    expect(wrapper.find('[data-testid="impact-used"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="impact-remaining"]').exists()).toBe(false);
  });
  it.each([401, 403, 404])('失败 %s 清除成功快照，不保留可误用的影响数值', async status => {
    const { wrapper } = panel(); await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    vi.mocked(getTenantQuotaUsage).mockRejectedValue({ status, type: 'about:blank', title: 'denied', code: 'test.denied' });
    await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="impact-used"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="subscription-quota-impact-problem"]').text()).not.toBe('');
  });
  it('503 保留明确时间的旧结果，错误企业响应则清除', async () => {
    const { wrapper } = panel(); await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    vi.mocked(getTenantQuotaUsage).mockRejectedValue({ status: 503, type: 'about:blank', title: 'unavailable' });
    await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    expect(wrapper.get('[data-testid="subscription-quota-impact-read-at"]').attributes('datetime')).toBe(summary.readAtUtc);
    expect(wrapper.get('[data-testid="subscription-quota-impact-problem"]').text()).toContain('上次成功读取');
    vi.mocked(getTenantQuotaUsage).mockResolvedValue({ ...summary, tenantId: id(20) });
    await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click'); await flushPromises();
    expect(wrapper.find('[data-testid="impact-used"]').exists()).toBe(false);
  });
  it.each(['tenant', 'offer', 'permission', 'session', 'disabled'])('%s 变化取消读取并拒绝迟到结果，不自动重读', async change => {
    let resolve!: (value: typeof summary) => void;
    vi.mocked(getTenantQuotaUsage).mockReturnValue(new Promise(done => { resolve = done; }));
    const { wrapper, session } = panel(); await wrapper.get('[data-testid="subscription-quota-impact-read"]').trigger('click');
    const signal = vi.mocked(getTenantQuotaUsage).mock.calls[0]![1]!;
    if (change === 'tenant') await wrapper.setProps({ tenantId: id(20) });
    else if (change === 'offer') await wrapper.setProps({ offer: { ...offer, version: 2, storageLimit: 500 } });
    else if (change === 'disabled') await wrapper.setProps({ disabled: true });
    else if (change === 'permission') session.currentUser!.permissions = [];
    else session.currentUser!.sessionId = id(30);
    await flushPromises(); expect(signal.aborted).toBe(true); resolve(summary); await flushPromises();
    expect(wrapper.find('[data-testid="impact-used"]').exists()).toBe(false); expect(getTenantQuotaUsage).toHaveBeenCalledTimes(1);
  });
  it('没有独立读取权限或切入租户时隐藏，失真报价上限拒绝比较', async () => {
    expect(panel([]).wrapper.find('[data-testid="subscription-quota-impact"]').exists()).toBe(false);
    const { wrapper, session } = panel(); await wrapper.setProps({ offer: { ...offer, storageLimit: Number.MAX_SAFE_INTEGER + 1 } });
    expect(wrapper.get('[data-testid="subscription-quota-impact-read"]').attributes('disabled')).toBeDefined();
    expect(wrapper.find('[data-testid="subscription-quota-impact-problem"]').exists()).toBe(true);
    session.currentUser!.tenantId = id(1); await flushPromises();
    expect(wrapper.find('[data-testid="subscription-quota-impact"]').exists()).toBe(false); expect(getTenantQuotaUsage).not.toHaveBeenCalled();
  });
});
