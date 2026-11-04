import { beforeEach, expect, it, vi } from 'vitest';
import * as api from './tenant-ownership';
import { request } from './http';
vi.mock('./http', () => ({ request: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000001';
const receipt = { tenantId: id, operationId: id, actorUserId: id, sourceMemberId: null, sourceUserId: null,
  sourcePreviousVersion: null, sourceVersion: null, targetMemberId: id, targetUserId: id, targetPreviousVersion: 1,
  targetVersion: 2, createdAtUtc: '2026-11-04T00:00:00Z' };
beforeEach(() => { vi.clearAllMocks(); });
it('写入明确关闭 401 刷新重放并传递取消信号', async () => {
  vi.mocked(request).mockResolvedValue(receipt); const cancellation = new AbortController();
  const body = { operationId: id, targetMemberId: id, targetVersion: 1, currentPassword: 'private-password', totpCode: '593712' };
  expect(await api.transferTenantOwner(body, cancellation.signal)).toEqual(receipt);
  expect(request).toHaveBeenCalledExactlyOnceWith('/api/v1/identity/tenant-members/ownership/transfer', expect.objectContaining({
    method: 'POST', body: JSON.stringify(body)
  }), cancellation.signal, { retryUnauthorized: false });
});
it('原操作核对只发 GET，不用交接写接口代替', async () => {
  vi.mocked(request).mockResolvedValue(receipt); await api.getTenantOwnerTransferReceipt(id);
  expect(request).toHaveBeenCalledExactlyOnceWith(`/api/v1/identity/tenant-members/ownership/operations/${id}`, { method: 'GET' }, undefined);
});
it.each([{ targetVersion: 1 }, { sourceMemberId: id }, { operationId: 'invalid' }, { createdAtUtc: 'invalid' }])('拒绝不完整或非下一版本回执：%j', async patch => {
  vi.mocked(request).mockResolvedValue({ ...receipt, ...patch }); await expect(api.getTenantOwnerTransferReceipt(id)).rejects.toThrow('client.invalid_tenant_owner_receipt');
});
