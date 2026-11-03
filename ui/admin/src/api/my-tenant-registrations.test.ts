import { beforeEach, expect, it, vi } from 'vitest';
import { listMyTenantRegistrations, continueMyTenantRegistration } from './my-tenant-registrations';
import { http } from './http';

vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '01981a3f-00c0-7000-8000-000000000010';
const row = { id, tenantId: id, registrationWayName: null, phase: 'Cancelled', errorCode: 'quota.rejected',
  createdAtUtc: '2026-11-04T00:00:00Z', version: 9, canContinue: true };
beforeEach(() => vi.clearAllMocks());
it('继续使用生成操作并固定版本，401 不自动重放写入', async () => {
  vi.mocked(http.request).mockResolvedValue(row);
  expect(await continueMyTenantRegistration(id, 7)).toEqual(row);
  expect(http.request).toHaveBeenCalledWith(`/api/v1/me/tenant-registrations/${id}/continue`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ version: 7 }) }, undefined, { retryUnauthorized: false });
});
it('保留读取取消信号，并拒绝未知业务阶段', async () => {
  const signal = new AbortController().signal;
  vi.mocked(http.request).mockResolvedValue([row]);
  expect(await listMyTenantRegistrations(signal)).toEqual([row]);
  expect(http.request).toHaveBeenCalledWith('/api/v1/me/tenant-registrations', { method: 'GET' }, signal);
  vi.mocked(http.request).mockResolvedValue([{ ...row, phase: 'Success' }]);
  await expect(listMyTenantRegistrations()).rejects.toThrow('client.invalid_registration_progress_list');
});
