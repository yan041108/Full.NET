import { describe, expect, it } from 'vitest';
import type { TenantQuotaUsageItemResponse } from '@fullnet/client-contracts';
import { quotaOfferImpactRows, quotaUsageRows } from './tenant-quota-usage';
const metric = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf201', metricCode: 'files.storage_bytes', periodKey: 'default',
  limitValue: '9223372036854775807', usedValue: '9223372036854775707', reservedValue: '25' };
describe('配额账面值的精确展示', () => {
  it('新报价影响按当前已用加预留计算，Int64 差额和未知指标不失真', () => {
    const rows = quotaOfferImpactRows([{ ...metric, usedValue: '9007199254740993', reservedValue: '3' }], 10, 1000)!;
    expect(rows[0]).toMatchObject({ configured: false, proposedLimit: '10', proposedRemaining: undefined });
    expect(rows[1]).toMatchObject({ proposedLimit: '1000', used: '9007199254740993', proposedRemaining: '-9007199254739996', overLimit: true });
    expect(quotaOfferImpactRows([{ ...metric, usedValue: '997', reservedValue: '3' }], 10, 1000)![1]).toMatchObject({ proposedRemaining: '0', overLimit: false });
  });
  it.each([-1, 0.5, Number.MAX_SAFE_INTEGER + 1, Number.NaN, Number.POSITIVE_INFINITY])('不能比较失真报价上限 %s', limit => {
    expect(quotaOfferImpactRows([metric], limit, 10)).toBeUndefined();
    expect(quotaOfferImpactRows([metric], 10, limit)).toBeUndefined();
  });
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
