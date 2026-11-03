import { beforeEach, expect, it, vi } from 'vitest';
import { getTenantLifecycle, changeTenantLifecycle } from './tenant-lifecycle';
import { http } from './http';

vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01981a3f-00c0-7000-8000-000000000010';
const row = { id, identifier: 'acme', domain: 'acme.localhost', name: 'Acme', isActive: false,
  version: 4, lifecycleStatus: 'Suspended', provisioningStatus: 'Completed' };
beforeEach(() => vi.clearAllMocks());
it('状态操作提交确认版本且 401 不自动重放', async () => {
  vi.mocked(http.request).mockResolvedValue(row);
  expect((await changeTenantLifecycle(id, 3, 'suspend')).lifecycleStatus).toBe('Suspended');
  const [path, request, signal, options] = vi.mocked(http.request).mock.calls[0]!;
  expect(path).toBe(`/api/v1/tenancy/tenants/${id}/suspend`);
  expect(request?.method).toBe('POST'); expect(JSON.parse(String(request?.body))).toEqual({ version: 3 });
  expect(signal).toBeUndefined(); expect(options).toEqual({ retryUnauthorized: false });
});
it('读取保留取消信号，并拒绝未知状态或终态活动标志', async () => {
  const signal = new AbortController().signal;
  vi.mocked(http.request).mockResolvedValue(row);
  await getTenantLifecycle(id, signal);
  expect(vi.mocked(http.request).mock.calls[0]![2]).toBe(signal);
  vi.mocked(http.request).mockResolvedValue({ ...row, lifecycleStatus: 'Closed', isActive: true });
  await expect(getTenantLifecycle(id)).rejects.toThrow('client.invalid_tenant_lifecycle');
  vi.mocked(http.request).mockResolvedValue({ ...row, lifecycleStatus: 'Unknown' });
  await expect(getTenantLifecycle(id)).rejects.toThrow('client.invalid_tenant_lifecycle');
});
