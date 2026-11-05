import { describe, expect, it } from 'vitest';
import type { TenantQuotaUsageItemResponse } from '@fullnet/client-contracts';
import { quotaUsageRows } from './tenant-quota-usage';
const metric = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf201', metricCode: 'files.storage_bytes', periodKey: 'default',
  limitValue: '9223372036854775807', usedValue: '9223372036854775707', reservedValue: '25' };
describe('配额账面值的精确展示', () => {
  it('Int64 上限仍能精确计算七十五余额', () => {
    const rows = quotaUsageRows([metric])!; const storage = rows.find(row => row.metricCode === 'files.storage_bytes')!;
    expect(storage.remaining).toBe('75'); expect(storage.limit).toBe('9223372036854775807'); expect(storage.overLimit).toBe(false);
  });
  it('未配置不补零，超额时保留负账面余额', () => {
    const rows = quotaUsageRows([{ ...metric, limitValue: '10', usedValue: '11', reservedValue: '3' }])!;
    expect(rows[0]).toMatchObject({ configured: false }); expect(rows[0].limit).toBeUndefined(); expect(rows[1]).toMatchObject({ remaining: '-4', overLimit: true });
  });
  it.each([100, null, undefined, '-1', '01', '+1', '1e3', '1.0', ' 1', '9223372036854775808', '9'.repeat(1000)])('无效或失真值 %s 拒绝展示', value => {
    expect(quotaUsageRows([{ ...metric, limitValue: value } as unknown as TenantQuotaUsageItemResponse])).toBeUndefined();
  });
  it('未知指标、其他周期和重复指标不能接入正式概览', () => {
    expect(quotaUsageRows([{ ...metric, metricCode: 'custom' }])).toBeUndefined();
    expect(quotaUsageRows([{ ...metric, periodKey: '2026-11' }])).toBeUndefined();
    expect(quotaUsageRows([metric, { ...metric, id: '019bc2b1-2a40-7cc3-8992-a80de51bf202' }])).toBeUndefined();
  });
});
