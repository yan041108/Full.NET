import {
  isOrganizationAssignableUserPage,
  isOrganizationUserPosition,
  isOrganizationUserPositionPage,
  organizationCreateTenantUserPosition,
  organizationDisableTenantUserPosition,
  organizationListAssignableTenantUserPositionUsers,
  organizationListTenantUserPositions,
  organizationUpdateTenantUserPosition,
  type OrganizationAssignableUserPage,
  type OrganizationUserPosition,
  type OrganizationUserPositionPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询可分配岗位关系的用户列表，供岗位成员分配弹窗复用。 */
export async function listAssignableOrganizationUserPositionUsers(
  page = 1,
  pageSize = 100,
  signal?: AbortSignal
): Promise<OrganizationAssignableUserPage> {
  const value = await organizationListAssignableTenantUserPositionUsers(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationAssignableUserPage(value)) {
    throw new Error('client.invalid_organization_assignable_user_page');
  }

  return value;
}

/** 分页查询用户岗位关系列表，并对响应页做失败关闭校验。 */
export async function listOrganizationUserPositions(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationUserPositionPage> {
  const value = await organizationListTenantUserPositions(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationUserPositionPage(value)) {
    throw new Error('client.invalid_organization_user_position_page');
  }

  return value;
}

/** 创建用户岗位关系，并可指定是否为主岗位。 */
export async function createOrganizationUserPosition(
  userId: string,
  positionId: string,
  isPrimary = false,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationCreateTenantUserPosition(
    http,
    { body: { userId, positionId, isPrimary } },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 更新用户岗位关系的主岗位标记，并携带版本号维持乐观并发。 */
export async function updateOrganizationUserPosition(
  id: string,
  isPrimary: boolean,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationUpdateTenantUserPosition(
    http,
    { assignmentId: id, body: { isPrimary, version } },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 禁用用户岗位关系。 */
export async function disableOrganizationUserPosition(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationDisableTenantUserPosition(
    http,
    { assignmentId: id },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 导出可分配用户页、用户岗位隶属详情与分页模型，供成员分配弹窗、列表页与主岗位切换流程共享同一契约。 */
export type {
  OrganizationAssignableUserPage,
  OrganizationUserPosition,
  OrganizationUserPositionPage
};
