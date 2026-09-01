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

/** 分页查询 Host 租户列表，并对生成守卫遗漏的标识符约束补失败关闭校验。 */
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

/** 创建 Host 租户，并可在创建时附带默认套餐。 */
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

/** 禁用 Host 租户。 */
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

/** 更新 Host 租户名称，并携带版本号维持乐观并发。 */
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

/** 为租户分配或清空套餐。 */
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

/** 导出租户详情与分页模型，供租户列表、开通弹窗与套餐分配流程共享同一契约。 */
export type { HostTenant, HostTenantPage };
