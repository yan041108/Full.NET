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

/** 分页查询 Host 租户套餐列表，并对生成守卫遗漏的编码约束补失败关闭校验。 */
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

/** 创建 Host 租户套餐，并把空白描述规整为 null。 */
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

/** 禁用 Host 租户套餐。 */
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

/** 更新 Host 租户套餐名称与描述，并携带版本号维持乐观并发。 */
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

/** 导出租户套餐详情与分页模型，供套餐列表、编辑弹窗与禁用流程共享同一契约。 */
export type { HostTenantPackage, HostTenantPackagePage };
