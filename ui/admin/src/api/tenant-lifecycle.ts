import { readTenantSummary, tenancyGetTenantLifecycle, tenancySuspendTenant, tenancyReactivateTenant, tenancyCloseTenant } from '@fullnet/client-contracts';
import { http } from './http';

export type TenantLifecycleAction = 'suspend' | 'reactivate' | 'close';
export const tenantLifecycleStatuses = ['Active', 'Suspended', 'Closing', 'Closed'] as const;
function readLifecycle(value: unknown) {
  const tenant = readTenantSummary(value);
  if (tenant.version <= 0 || !tenantLifecycleStatuses.some(status => status === tenant.lifecycleStatus)
    || !['Pending', 'InProgress', 'Completed', 'Failed'].some(status => status === tenant.provisioningStatus)
    || (tenant.lifecycleStatus !== 'Active' && tenant.isActive)) throw new TypeError('client.invalid_tenant_lifecycle');
  return { ...tenant, lifecycleStatus: tenant.lifecycleStatus as typeof tenantLifecycleStatuses[number], provisioningStatus: tenant.provisioningStatus! };
}
export async function getTenantLifecycle(id: string, signal?: AbortSignal) {
  return readLifecycle(await tenancyGetTenantLifecycle(http, { tenantId: id }, signal));
}
/** 状态写入固定用户确认的版本，认证过期也不能自动重放。 */
export async function changeTenantLifecycle(id: string, version: number, action: TenantLifecycleAction) {
  const operation = { suspend: tenancySuspendTenant, reactivate: tenancyReactivateTenant, close: tenancyCloseTenant }[action];
  return readLifecycle(await operation(http, { tenantId: id, body: { version } }, undefined, { retryUnauthorized: false }));
}
