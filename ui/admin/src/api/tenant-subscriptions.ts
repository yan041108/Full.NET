import type { TenantSubscription, CreateTenantSubscriptionBody, PagedResult } from '@fullnet/client-contracts';
import { request } from './http';
import { isRecord, isGuid, isInteger, isNullableDate, readResponse } from '@fullnet/client-contracts';
export type { TenantSubscription, CreateTenantSubscriptionBody } from '@fullnet/client-contracts';
function isSubscription(v: unknown): v is TenantSubscription { return isRecord(v) && isGuid(v.id) && isGuid(v.tenantId) && (v.packageId === null || v.packageId === undefined || isGuid(v.packageId)) && typeof v.status === 'string' && isNullableDate(v.trialEndsAtUtc ?? null) && isNullableDate(v.cancelledAtUtc ?? null) && typeof v.currentPeriodStartUtc === 'string' && typeof v.currentPeriodEndUtc === 'string' && isInteger(v.version); }

export async function listTenantSubscriptions(tenantId: string, page = 1, pageSize = 20): Promise<PagedResult<TenantSubscription>> {
  const value = await request<unknown>(`/api/v1/tenancy/tenants/${tenantId}/subscriptions?page=${page}&pageSize=${pageSize}`);
  return readResponse(value, (v): v is PagedResult<TenantSubscription> => isRecord(v) && Array.isArray(v.items) && v.items.every(isSubscription) && isInteger(v.page) && isInteger(v.pageSize) && typeof v.total === 'number', 'client.invalid_tenant_subscription_list');
}

export async function createTenantSubscription(
  tenantId: string,
  body: CreateTenantSubscriptionBody
): Promise<TenantSubscription> {
  const value = await request<unknown>(`/api/v1/tenancy/tenants/${tenantId}/subscriptions`, {
    method: 'POST',
    body: JSON.stringify(body)
  });
  return readResponse(value, isSubscription, 'client.invalid_tenant_subscription');
}

export async function cancelTenantSubscription(
  tenantId: string,
  subscriptionId: string,
  version: number
): Promise<TenantSubscription> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/cancel`,
    {
      method: 'POST',
      body: JSON.stringify({ version })
    }
  );
  return readResponse(value, isSubscription, 'client.invalid_tenant_subscription');
}
