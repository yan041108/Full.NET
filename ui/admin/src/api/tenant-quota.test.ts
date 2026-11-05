import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { previewTenantQuotaUsage } from './tenant-quota';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
beforeEach(() => vi.resetAllMocks());
it('固定预检及每批 25 家，原样传递游标与取消信号', async () => {
  vi.mocked(http.request).mockResolvedValue({ dryRun: true, appliedCount: 0, candidateCount: 0, tenantIds: [], isComplete: true, nextAfterTenantId: null });
  const signal = new AbortController().signal; const after = '019bc2b1-2a40-7cc3-8992-a80de51bf201';
  await previewTenantQuotaUsage('files.storage_bytes', after, signal);
  expect(http.request).toHaveBeenCalledWith('/api/v1/tenancy/quota/usage-baseline', {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ dryRun: true, metricCode: 'files.storage_bytes', afterTenantId: after, pageSize: 25 })
  }, signal);
});
