import type { TenantQuotaUsageItemResponse } from '@fullnet/client-contracts';

export const quotaMetricCodes = ['identity.seats', 'files.storage_bytes'] as const;
export type QuotaMetricCode = typeof quotaMetricCodes[number];
export interface QuotaUsageRow {
  metricCode: QuotaMetricCode;
  configured: boolean;
  limit?: string;
  used?: string;
  reserved?: string;
  remaining?: string;
  overLimit: boolean;
}
export interface QuotaOfferImpactRow extends QuotaUsageRow {
  proposedLimit: string;
  proposedRemaining?: string;
}
const maximum = 9223372036854775807n;
function exact(value: unknown): value is string {
  return typeof value === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(value) && BigInt(value) <= maximum;
}
/** 只解释正式非周期账面指标；用 BigInt 计算，不把未配置或无效数据补成零。 */
export function quotaUsageRows(metrics: readonly TenantQuotaUsageItemResponse[]): QuotaUsageRow[] | undefined {
  if (metrics.length > 2 || new Set(metrics.map(item => item.id.toLowerCase())).size !== metrics.length) return undefined;
  const rows = new Map<QuotaMetricCode, QuotaUsageRow>();
  for (const metric of metrics) {
    if (!(quotaMetricCodes as readonly string[]).includes(metric.metricCode) || metric.periodKey !== 'default'
      || rows.has(metric.metricCode as QuotaMetricCode) || !exact(metric.limitValue) || !exact(metric.usedValue) || !exact(metric.reservedValue)) return undefined;
    const remaining = BigInt(metric.limitValue) - BigInt(metric.usedValue) - BigInt(metric.reservedValue);
    rows.set(metric.metricCode as QuotaMetricCode, { metricCode: metric.metricCode as QuotaMetricCode, configured: true,
      limit: metric.limitValue, used: metric.usedValue, reserved: metric.reservedValue, remaining: remaining.toString(), overLimit: remaining < 0n });
  }
  return quotaMetricCodes.map(metricCode => rows.get(metricCode) ?? { metricCode, configured: false, overLimit: false });
}

/** 报价仍使用安全整数；账面用量和差额始终按 BigInt 计算，不预测未来时点的实际准入。 */
export function quotaOfferImpactRows(metrics: readonly TenantQuotaUsageItemResponse[], seatLimit: number,
  storageLimit: number): QuotaOfferImpactRow[] | undefined {
  if (![seatLimit, storageLimit].every(value => Number.isSafeInteger(value) && value >= 0)) return undefined;
  const rows = quotaUsageRows(metrics);
  return rows?.map(row => {
    const proposedLimit = String(row.metricCode === 'identity.seats' ? seatLimit : storageLimit);
    const proposedRemaining = row.configured ? (BigInt(proposedLimit) - BigInt(row.used!) - BigInt(row.reserved!)).toString() : undefined;
    return { ...row, proposedLimit, proposedRemaining, overLimit: proposedRemaining !== undefined && BigInt(proposedRemaining) < 0n };
  });
}
