import { tenancyGetTenantQuotaUsage, tenancyReconcileQuotaUsageBaseline, type TenantQuotaUsageSummaryResponse } from '@fullnet/client-contracts';
import { http } from './http';

export type { TenantQuotaUsageSummaryResponse } from '@fullnet/client-contracts';
export const getTenantQuotaUsage = (tenantId: string, signal?: AbortSignal): Promise<TenantQuotaUsageSummaryResponse> =>
  tenancyGetTenantQuotaUsage(http, { tenantId }, signal);

/** 控制台仅允许有界预检，不向调用方开放修复开关。 */
export const previewTenantQuotaUsage = (metricCode: 'identity.seats' | 'files.storage_bytes', afterTenantId: string | null, signal?: AbortSignal) =>
  tenancyReconcileQuotaUsageBaseline(http, { body: { dryRun: true, metricCode, afterTenantId, pageSize: 25 } }, signal);
