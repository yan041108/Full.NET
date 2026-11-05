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

it('权威诊断仅对原企业发 GET，传递指标和取消信号且没有修复正文', async () => {
  const { getTenantQuotaUsageDiagnostic } = await import('./tenant-quota');
  const target = '019bc2b1-2a40-7cc3-8992-a80de51bf201'; const signal = new AbortController().signal;
  vi.mocked(http.request).mockResolvedValue({ tenantId: target, metricCode: 'files.storage_bytes', tenantIsActive: true,
    metricId: '019bc2b1-2a40-7cc3-8992-a80de51bf202', bookUsedValue: '2', reservedValue: '0', authoritativeUsedValue: '3', observedDifference: '1', state: 'Different', readAtUtc: '2026-11-05T00:00:00Z' });
  await getTenantQuotaUsageDiagnostic(target, 'files.storage_bytes', signal);
  const [url, init, actualSignal] = vi.mocked(http.request).mock.calls[0]!;
  expect(url).toContain('/api/v1/tenancy/tenants/' + target + '/quota/usage-diagnostic?');
  expect(new URL(url, 'https://example.invalid').searchParams.get('metricCode')).toBe('files.storage_bytes');
  expect(init?.method).toBe('GET'); expect(init?.body).toBeUndefined(); expect(actualSignal).toBe(signal);
});
