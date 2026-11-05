import { describe, expect, it } from 'vitest';
import { validQuotaPreview } from './tenant-quota-preview';
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf201';
const batch = { dryRun: true, appliedCount: 0, candidateCount: 1, tenantIds: [id], isComplete: false, nextAfterTenantId: id };
describe('配额预检响应边界', () => {
  it('接受首批及显式末批，不要求 UUID 字符顺序', () => {
    expect(validQuotaPreview(batch, 'f19bc2b1-2a40-7cc3-8992-a80de51bf201')).toBe(true);
    expect(validQuotaPreview({ ...batch, isComplete: true, nextAfterTenantId: null }, id)).toBe(true);
  });
  it.each([
    { dryRun: false }, { appliedCount: 1 }, { candidateCount: 26 }, { candidateCount: -1 }, { candidateCount: 0.5 },
    { candidateCount: 0 }, { tenantIds: [id, id], candidateCount: 2 }, { tenantIds: ['bad'] },
    { nextAfterTenantId: null }, { nextAfterTenantId: 'bad' }, { isComplete: true }
  ])('拒绝损坏批次 %j', patch => expect(validQuotaPreview({ ...batch, ...patch }, null)).toBe(false));
  it('拒绝原地重复游标', () => expect(validQuotaPreview(batch, id.toUpperCase())).toBe(false));
});
