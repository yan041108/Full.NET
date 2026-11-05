import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { getTenantSubscription, listTenantSubscriptions } from './tenant-subscriptions';
vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
it('独立读取原订阅，目标身份与取消信号不依赖列表', async () => {
  const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf201'; const subscription = '019bc2b1-2a40-7cc3-8992-a80de51bf202';
  const signal = new AbortController().signal;
  vi.mocked(http.request).mockResolvedValue({ id: subscription, tenantId: tenant, packageId: null, status: 'Expired', trialEndsAtUtc: null,
    currentPeriodStartUtc: '2026-10-01T00:00:00Z', currentPeriodEndUtc: '2026-11-01T00:00:00Z', cancelledAtUtc: null, version: 2 });
  await getTenantSubscription(tenant, subscription, signal);
  expect(http.request).toHaveBeenCalledWith(`/api/v1/tenancy/tenants/${tenant}/subscriptions/${subscription}`, { method: 'GET' }, signal);
});
it.each(['Expired', ''])('生成列表操作保留分页、状态 %s 与取消信号', async status => {
  vi.mocked(http.request).mockResolvedValue({ items: [], page: 2, pageSize: 20, total: 0 });
  const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf201'; const signal = new AbortController().signal;
  await listTenantSubscriptions(tenant, 2, 20, signal, status);
  const [path, init, actualSignal] = vi.mocked(http.request).mock.calls[0]!; const url = new URL(path, 'https://example.test');
  expect(url.pathname).toBe(`/api/v1/tenancy/tenants/${tenant}/subscriptions`); expect(url.searchParams.get('page')).toBe('2');
  expect(url.searchParams.get('pageSize')).toBe('20'); expect(url.searchParams.get('status')).toBe(status || null);
  expect(init?.method).toBe('GET'); expect(actualSignal).toBe(signal);
});
