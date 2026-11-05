import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { listTenantSubscriptions } from './tenant-subscriptions';
vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
it.each(['Expired', ''])('生成列表操作保留分页、状态 %s 与取消信号', async status => {
  vi.mocked(http.request).mockResolvedValue({ items: [], page: 2, pageSize: 20, total: 0 });
  const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf201'; const signal = new AbortController().signal;
  await listTenantSubscriptions(tenant, 2, 20, signal, status);
  const [path, init, actualSignal] = vi.mocked(http.request).mock.calls[0]!; const url = new URL(path, 'https://example.test');
  expect(url.pathname).toBe(`/api/v1/tenancy/tenants/${tenant}/subscriptions`); expect(url.searchParams.get('page')).toBe('2');
  expect(url.searchParams.get('pageSize')).toBe('20'); expect(url.searchParams.get('status')).toBe(status || null);
  expect(init?.method).toBe('GET'); expect(actualSignal).toBe(signal);
});
