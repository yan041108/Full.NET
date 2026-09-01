import {
  isHostUserOrganizationReference,
  isOrganizationUserPosition,
  isOrganizationUserUnit,
  organizationCreateHostUserManagementUserPosition,
  organizationCreateHostUserManagementUserUnit,
  organizationDisableHostUserManagementUserPosition,
  organizationDisableHostUserManagementUserUnit,
  organizationGetHostUserManagementReference,
  organizationUpdateHostUserManagementUserPosition,
  organizationUpdateHostUserManagementUserUnit,
  type HostUserOrganizationReference,
  type OrganizationUserPosition,
  type OrganizationUserUnit
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询 Host 用户管理页所需的组织参考数据，并对响应结构做失败关闭校验。 */
export async function getHostUserOrganizationReference(
  tenantId: string,
  signal?: AbortSignal
): Promise<HostUserOrganizationReference> {
  const value = await organizationGetHostUserManagementReference(
    http,
    { tenantId },
    signal
  );
  if (!isHostUserOrganizationReference(value)) {
    throw new Error('client.invalid_host_user_organization_reference');
  }

  return value;
}

/** 为 Host 用户创建机构隶属关系。 */
export async function createHostUserOrganizationUnit(
  tenantId: string,
  userId: string,
  unitId: string,
  isPrimary = false,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationCreateHostUserManagementUserUnit(
    http,
    { tenantId, body: { userId, unitId, isPrimary } },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 更新 Host 用户机构隶属关系的主机构标记，并维持版本并发控制。 */
export async function updateHostUserOrganizationUnit(
  tenantId: string,
  assignmentId: string,
  isPrimary: boolean,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationUpdateHostUserManagementUserUnit(
    http,
    { tenantId, assignmentId, body: { isPrimary, version } },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 禁用 Host 用户机构隶属关系。 */
export async function disableHostUserOrganizationUnit(
  tenantId: string,
  assignmentId: string,
  signal?: AbortSignal
): Promise<OrganizationUserUnit> {
  const value = await organizationDisableHostUserManagementUserUnit(
    http,
    { tenantId, assignmentId },
    signal
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }

  return value;
}

/** 为 Host 用户创建岗位隶属关系。 */
export async function createHostUserOrganizationPosition(
  tenantId: string,
  userId: string,
  positionId: string,
  isPrimary = false,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationCreateHostUserManagementUserPosition(
    http,
    { tenantId, body: { userId, positionId, isPrimary } },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 更新 Host 用户岗位隶属关系的主岗位标记，并维持版本并发控制。 */
export async function updateHostUserOrganizationPosition(
  tenantId: string,
  assignmentId: string,
  isPrimary: boolean,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationUpdateHostUserManagementUserPosition(
    http,
    { tenantId, assignmentId, body: { isPrimary, version } },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 禁用 Host 用户岗位隶属关系。 */
export async function disableHostUserOrganizationPosition(
  tenantId: string,
  assignmentId: string,
  signal?: AbortSignal
): Promise<OrganizationUserPosition> {
  const value = await organizationDisableHostUserManagementUserPosition(
    http,
    { tenantId, assignmentId },
    signal
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }

  return value;
}

/** 导出 Host 用户组织参考与机构/岗位隶属模型，供多步保存链路共享同一契约。 */
export type {
  HostUserOrganizationReference,
  OrganizationUserPosition,
  OrganizationUserUnit
};
