import { readResponse, isTenantOwnershipState, isTenantOwnerTransferReceipt,
  type TenantOwnershipState, type TenantOwnerTransferRequest, type TenantOwnerTransferReceipt } from '@fullnet/client-contracts';
import { request } from './http';
export async function getTenantOwnership(signal?: AbortSignal): Promise<TenantOwnershipState> {
  return readResponse(await request<unknown>('/api/v1/identity/tenant-members/ownership', { method: 'GET' }, signal), isTenantOwnershipState, 'client.invalid_tenant_ownership');
}
export async function getTenantOwnerTransferReceipt(operationId: string, signal?: AbortSignal): Promise<TenantOwnerTransferReceipt> {
  return readResponse(await request<unknown>(`/api/v1/identity/tenant-members/ownership/operations/${encodeURIComponent(operationId)}`,
    { method: 'GET' }, signal), isTenantOwnerTransferReceipt, 'client.invalid_tenant_owner_receipt');
}
export async function transferTenantOwner(body: TenantOwnerTransferRequest, signal?: AbortSignal): Promise<TenantOwnerTransferReceipt> {
  return readResponse(await request<unknown>('/api/v1/identity/tenant-members/ownership/transfer', {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body)
  }, signal, { retryUnauthorized: false }), isTenantOwnerTransferReceipt, 'client.invalid_tenant_owner_receipt');
}
