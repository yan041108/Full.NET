import type { TenantSubscription, CreateTenantSubscriptionBody, PagedResult } from '@fullnet/client-contracts';
import { http, request } from './http';
import { tenancyListTenantSubscriptions } from '@fullnet/client-contracts';
import { isRecord, isGuid, isInteger, isNullableDate, readResponse } from '@fullnet/client-contracts';
export type { TenantSubscription, CreateTenantSubscriptionBody } from '@fullnet/client-contracts';
function isSubscription(v: unknown): v is TenantSubscription { return isRecord(v) && isGuid(v.id) && isGuid(v.tenantId) && (v.packageId === null || v.packageId === undefined || isGuid(v.packageId)) && typeof v.status === 'string' && isNullableDate(v.trialEndsAtUtc ?? null) && isNullableDate(v.cancelledAtUtc ?? null) && typeof v.currentPeriodStartUtc === 'string' && typeof v.currentPeriodEndUtc === 'string' && isInteger(v.version); }

export async function listTenantSubscriptions(tenantId: string, page = 1, pageSize = 20, signal?: AbortSignal, status?: string): Promise<PagedResult<TenantSubscription>> {
  return tenancyListTenantSubscriptions(http, { tenantId, page, pageSize, status: status || undefined }, signal);
}

export async function createTenantSubscription(
  tenantId: string,
  body: CreateTenantSubscriptionBody, signal?: AbortSignal
): Promise<TenantSubscription> {
  const value = await request<unknown>(`/api/v1/tenancy/tenants/${tenantId}/subscriptions`, {
    method: 'POST',
    signal, body: JSON.stringify(body)
  });
  return readResponse(value, isSubscription, 'client.invalid_tenant_subscription');
}

export async function cancelTenantSubscription(
  tenantId: string,
  subscriptionId: string,
  version: number, signal?: AbortSignal
): Promise<TenantSubscription> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/cancel`,
    {
      method: 'POST',
      signal, body: JSON.stringify({ version })
    }
  );
  return readResponse(value, isSubscription, 'client.invalid_tenant_subscription');
}
