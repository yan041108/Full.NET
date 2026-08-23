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
