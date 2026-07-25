import {
  isHostTenant,
  isHostTenantPage,
  type HostTenant,
  type HostTenantPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostTenants(
  page = 1,
  pageSize = 20
): Promise<HostTenantPage> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostTenantPage(value)) throw new Error('client.invalid_host_tenant_page');
  return value;
}

export async function createHostTenant(
  identifier: string,
  name: string,
  domain: string,
  tenantPackageId?: string | null
): Promise<HostTenant> {
  const body: Record<string, string | null> = { identifier, name, domain };
  if (tenantPackageId) {
    body.tenantPackageId = tenantPackageId;
  }
  const value = await request<unknown>('/api/v1/tenancy/tenants', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!isHostTenant(value)) throw new Error('client.invalid_host_tenant');
  return value;
}

export async function disableHostTenant(id: string): Promise<HostTenant> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostTenant(value)) throw new Error('client.invalid_host_tenant');
  return value;
}

export async function updateHostTenant(
  id: string,
  name: string,
  version: number
): Promise<HostTenant> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, version })
    }
  );
  if (!isHostTenant(value)) throw new Error('client.invalid_host_tenant');
  return value;
}

export async function assignHostTenantPackage(
  tenantId: string,
  tenantPackageId: string | null,
  version: number
): Promise<HostTenant> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenants/${encodeURIComponent(tenantId)}/package`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ tenantPackageId, version })
    }
  );
  if (!isHostTenant(value)) throw new Error('client.invalid_host_tenant');
  return value;
}
