import { describe, expect, it } from 'vitest';
import { readTenantQuotaUsageSummaryResponse } from '../src/generated/guards.generated';
const value = { tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf201', tenantIsActive: true, readAtUtc: '2026-11-05T00:00:00Z', metrics: [
  { id: '019bc2b1-2a40-7cc3-8992-a80de51bf202', metricCode: 'files.storage_bytes', periodKey: 'default',
    limitValue: '9223372036854775807', usedValue: '9223372036854775707', reservedValue: '25' }] };
describe('配额摘要生成客户端线格式', () => {
  it('保留完整 Int64 十进制字符串及空指标列表', () => {
    expect(readTenantQuotaUsageSummaryResponse(value).metrics[0]!.limitValue).toBe('9223372036854775807');
    expect(readTenantQuotaUsageSummaryResponse({ ...value, metrics: [] }).metrics).toEqual([]);
  });
  it.each(['limitValue', 'usedValue', 'reservedValue'])('拒绝 %s 数字令牌以避免先行舍入', field => {
    expect(() => readTenantQuotaUsageSummaryResponse({ ...value, metrics: [{ ...value.metrics[0], [field]: 9007199254740992 }] })).toThrow();
  });
});
