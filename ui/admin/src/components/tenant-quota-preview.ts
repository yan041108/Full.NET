import { isGuid, type ReconcileTenantQuotaUsageBaselineResponse } from '@fullnet/client-contracts';

/** 游标由提供程序定义顺序；只核对身份和推进，不按 UUID 文本排序。 */
export function validQuotaPreview(value: ReconcileTenantQuotaUsageBaselineResponse, after: string | null): boolean {
  if (!value || value.dryRun !== true || value.appliedCount !== 0 || !Number.isInteger(value.candidateCount)
    || value.candidateCount < 0 || value.candidateCount > 25 || !Array.isArray(value.tenantIds)
    || value.tenantIds.length > value.candidateCount || !value.tenantIds.every(id => isGuid(id))
    || new Set(value.tenantIds.map(id => id.toLowerCase())).size !== value.tenantIds.length) return false;
  if (value.isComplete === true) return value.nextAfterTenantId === null;
  return value.isComplete === false && typeof value.nextAfterTenantId === 'string' && isGuid(value.nextAfterTenantId)
    && value.nextAfterTenantId.toLowerCase() !== after?.toLowerCase();
}
