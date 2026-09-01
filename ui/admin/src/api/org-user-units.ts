import {
  isOrganizationAssignableUserPage,
  isOrganizationUserUnit,
  isOrganizationUserUnitPage,
  organizationCreateTenantUserUnit,
  organizationDisableTenantUserUnit,
  organizationListAssignableTenantUserUnitUsers,
  organizationListTenantUserUnits,
  organizationUpdateTenantUserUnit,
  type OrganizationAssignableUserPage,
  type OrganizationUserUnit,
  type OrganizationUserUnitPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询可分配机构关系的用户列表，供机构成员分配弹窗复用。 */
export async function listAssignableOrganizationUserUnitUsers(
  page = 1,
  pageSize = 100,
  signal?: AbortSignal
): Promise<OrganizationAssignableUserPage> {
  const value = await organizationListAssignableTenantUserUnitUsers(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationAssignableUserPage(value)) {
    throw new Error('client.invalid_organization_assignable_user_page');
  }

  return value;
}

/** 分页查询用户机构关系列表，并对响应页做失败关闭校验。 */
export async function listOrganizationUserUnits(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationUserUnitPage> {
  const value = await organizationListTenantUserUnits(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationUserUnitPage(value)) {
    throw new Error('client.invalid_organization_user_unit_page');
  }

  return value;
}

/** 创建用户机构关系，并可指定是否为主机构。 */
export async function createOrganizationUserUnit(
  userId: string,
  unitId: string,
  isPrimary = false,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationCreateTenantUserUnit(
    http,
    { body: { userId, unitId, isPrimary } },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 更新用户机构关系的主机构标记，并携带版本号维持乐观并发。 */
export async function updateOrganizationUserUnit(
  id: string,
  isPrimary: boolean,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationUpdateTenantUserUnit(
    http,
    { assignmentId: id, body: { isPrimary, version } },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 禁用用户机构关系。 */
export async function disableOrganizationUserUnit(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationDisableTenantUserUnit(
    http,
    { assignmentId: id },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 导出可分配用户页、用户机构隶属详情与分页模型，供成员分配弹窗、列表页与主机构切换流程共享同一契约。 */
export type {
  OrganizationAssignableUserPage,
  OrganizationUserUnit,
  OrganizationUserUnitPage
};
