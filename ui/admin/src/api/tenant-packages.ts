import {
  isHostTenantPackage,
  isHostTenantPackagePage,
  tenancyCreateHostTenantPackage,
  tenancyDisableHostTenantPackage,
  tenancyListHostTenantPackages,
  tenancyUpdateHostTenantPackage,
  type HostTenantPackage,
  type HostTenantPackagePage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listHostTenantPackages(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostTenantPackagePage> {
  const value = await tenancyListHostTenantPackages(
    http,
    { page, pageSize },
    signal
  );
  // 生成守卫不校验 code 模式；页面仍要求手写契约。
  if (!isHostTenantPackagePage(value)) {
    throw new Error('client.invalid_host_tenant_package_page');
  }

  return value;
}

export async function createHostTenantPackage(
  code: string,
  name: string,
  description?: string | null,
  signal?: AbortSignal
): Promise<HostTenantPackage> {
  const value = await tenancyCreateHostTenantPackage(
    http,
    {
      body: {
        code,
        name,
        description: description?.trim() ? description.trim() : null
      }
    },
    signal
  );
  if (!isHostTenantPackage(value)) {
    throw new Error('client.invalid_host_tenant_package');
  }

  return value;
}

export async function disableHostTenantPackage(
  id: string,
  signal?: AbortSignal
): Promise<HostTenantPackage> {
  const value = await tenancyDisableHostTenantPackage(
    http,
    { packageId: id },
    signal
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
  version: number,
  signal?: AbortSignal
): Promise<HostTenantPackage> {
  const value = await tenancyUpdateHostTenantPackage(
    http,
    { packageId: id, body: { name, description, version } },
    signal
  );
  if (!isHostTenantPackage(value)) {
    throw new Error('client.invalid_host_tenant_package');
  }

  return value;
}
