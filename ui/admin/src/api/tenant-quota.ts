import { tenancyGetTenantQuotaUsage, type TenantQuotaUsageSummaryResponse } from '@fullnet/client-contracts';
import { http } from './http';

export type { TenantQuotaUsageSummaryResponse } from '@fullnet/client-contracts';
export const getTenantQuotaUsage = (tenantId: string, signal?: AbortSignal): Promise<TenantQuotaUsageSummaryResponse> =>
  tenancyGetTenantQuotaUsage(http, { tenantId }, signal);
