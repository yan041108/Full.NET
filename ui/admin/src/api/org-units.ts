import {
  isOrganizationUnit,
  isOrganizationUnitPage,
  organizationCreateTenantUnit,
  organizationDisableTenantUnit,
  organizationListTenantUnits,
  organizationUpdateTenantUnit,
  type OrganizationUnit,
  type OrganizationUnitPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询机构列表，并对响应页做失败关闭校验。 */
export async function listOrganizationUnits(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationUnitPage> {
  const value = await organizationListTenantUnits(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationUnitPage(value)) {
    throw new Error('client.invalid_organization_unit_page');
  }

  return value;
}

/** 创建机构，可在创建时直接指定上级机构。 */
export async function createOrganizationUnit(
  code: string,
  name: string,
  displayOrder = 10,
  parentId: string | null = null,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationCreateTenantUnit(
    http,
    { body: { parentId, code, name, displayOrder } },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}

/** 更新机构名称、排序与上级机构，并携带版本号维持乐观并发。 */
export async function updateOrganizationUnit(
  id: string,
  name: string,
  displayOrder: number,
  version: number,
  parentId: string | null = null,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationUpdateTenantUnit(
    http,
    { unitId: id, body: { parentId, name, displayOrder, version } },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}

/** 禁用机构。 */
export async function disableOrganizationUnit(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationDisableTenantUnit(
    http,
    { unitId: id },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}

/** 导出机构详情与分页模型，供机构列表、树选择器与编辑弹窗共享同一契约。 */
export type { OrganizationUnit, OrganizationUnitPage };
