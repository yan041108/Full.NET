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
