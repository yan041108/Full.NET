import {
  isHostTenant,
  isHostTenantPage,
  tenancyAssignHostTenantPackage,
  tenancyCreateHostTenant,
  tenancyDisableHostTenant,
  tenancyListHostTenants,
  tenancyUpdateHostTenant,
  type HostTenant,
  type HostTenantPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listHostTenants(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostTenantPage> {
  const value = await tenancyListHostTenants(http, { page, pageSize }, signal);
  // 生成守卫不校验 identifier 模式与非空域名；页面仍要求手写契约。
  if (!isHostTenantPage(value)) {
    throw new Error('client.invalid_host_tenant_page');
  }

  return value;
}

export async function createHostTenant(
  identifier: string,
  name: string,
  domain: string,
  tenantPackageId?: string | null,
  signal?: AbortSignal
): Promise<HostTenant> {
  const value = await tenancyCreateHostTenant(
    http,
    {
      body: {
        identifier,
        name,
        domain,
        ...(tenantPackageId ? { tenantPackageId } : {})
      }
    },
    signal
  );
  if (!isHostTenant(value)) {
    throw new Error('client.invalid_host_tenant');
  }

  return value;
}

export async function disableHostTenant(
  id: string,
  signal?: AbortSignal
): Promise<HostTenant> {
  const value = await tenancyDisableHostTenant(http, { tenantId: id }, signal);
  if (!isHostTenant(value)) {
    throw new Error('client.invalid_host_tenant');
  }

  return value;
}

export async function updateHostTenant(
  id: string,
  name: string,
  version: number,
  signal?: AbortSignal
): Promise<HostTenant> {
  const value = await tenancyUpdateHostTenant(
    http,
    { tenantId: id, body: { name, version } },
    signal
  );
  if (!isHostTenant(value)) {
    throw new Error('client.invalid_host_tenant');
  }

  return value;
}

export async function assignHostTenantPackage(
  tenantId: string,
  tenantPackageId: string | null,
  version: number,
  signal?: AbortSignal
): Promise<HostTenant> {
  const value = await tenancyAssignHostTenantPackage(
    http,
    { tenantId, body: { tenantPackageId, version } },
    signal
  );
  if (!isHostTenant(value)) {
    throw new Error('client.invalid_host_tenant');
  }

  return value;
}
