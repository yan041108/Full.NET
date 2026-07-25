import {
  isHostTenantPackage,
  isHostTenantPackagePage,
  type HostTenantPackage,
  type HostTenantPackagePage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostTenantPackages(
  page = 1,
  pageSize = 20
): Promise<HostTenantPackagePage> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenant-packages?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostTenantPackagePage(value)) {
    throw new Error('client.invalid_host_tenant_package_page');
  }
  return value;
}

export async function createHostTenantPackage(
  code: string,
  name: string,
  description?: string | null
): Promise<HostTenantPackage> {
  const value = await request<unknown>('/api/v1/tenancy/tenant-packages', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      code,
      name,
      description: description?.trim() ? description.trim() : null
    })
  });
  if (!isHostTenantPackage(value)) {
    throw new Error('client.invalid_host_tenant_package');
  }
  return value;
}

export async function disableHostTenantPackage(
  id: string
): Promise<HostTenantPackage> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenant-packages/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostTenantPackage(value)) {
    throw new Error('client.invalid_host_tenant_package');
  }
  return value;
}

export async function updateHostTenantPackage(
  id: string,
  name: string,
  description: string | null,
  version: number
): Promise<HostTenantPackage> {
  const value = await request<unknown>(
    `/api/v1/tenancy/tenant-packages/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, description, version })
    }
  );
  if (!isHostTenantPackage(value)) {
    throw new Error('client.invalid_host_tenant_package');
  }
  return value;
}
