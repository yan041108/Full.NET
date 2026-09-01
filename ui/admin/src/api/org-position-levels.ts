import {
  isOrganizationPositionLevel,
  isOrganizationPositionLevelPage,
  organizationCreateTenantPositionLevel,
  organizationDisableTenantPositionLevel,
  organizationListTenantPositionLevels,
  organizationUpdateTenantPositionLevel,
  type OrganizationPositionLevel,
  type OrganizationPositionLevelPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询岗位级别列表，并对响应页做失败关闭校验。 */
export async function listOrganizationPositionLevels(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationPositionLevelPage> {
  const value = await organizationListTenantPositionLevels(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationPositionLevelPage(value)) {
    throw new Error('client.invalid_organization_position_level_page');
  }

  return value;
}

/** 创建岗位级别。 */
export async function createOrganizationPositionLevel(
  code: string,
  name: string,
  displayOrder = 10,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationCreateTenantPositionLevel(
    http,
    { body: { code, name, displayOrder } },
    signal
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }

  return value;
}

/** 更新岗位级别名称与排序，并携带版本号维持乐观并发。 */
export async function updateOrganizationPositionLevel(
  id: string,
  name: string,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationUpdateTenantPositionLevel(
    http,
    { positionLevelId: id, body: { name, displayOrder, version } },
    signal
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }

  return value;
}

/** 禁用岗位级别。 */
export async function disableOrganizationPositionLevel(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationDisableTenantPositionLevel(
    http,
    { positionLevelId: id },
    signal
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }

  return value;
}

/** 导出岗位级别详情与分页模型，供级别列表、编辑弹窗与岗位级别选择器共享同一契约。 */
export type { OrganizationPositionLevel, OrganizationPositionLevelPage };
