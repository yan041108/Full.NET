import {
  isOrganizationAssignableUserPage,
  isOrganizationUserPosition,
  isOrganizationUserPositionPage,
  type OrganizationAssignableUserPage,
  type OrganizationUserPosition,
  type OrganizationUserPositionPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAssignableOrganizationUserPositionUsers(
  page = 1,
  pageSize = 100
): Promise<OrganizationAssignableUserPage> {
  const value = await request<unknown>(
    `/api/v1/organization/user-positions/assignable-users?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationAssignableUserPage(value)) {
    throw new Error('client.invalid_organization_assignable_user_page');
  }
  return value;
}

export async function listOrganizationUserPositions(
  page = 1,
  pageSize = 20
): Promise<OrganizationUserPositionPage> {
  const value = await request<unknown>(
    `/api/v1/organization/user-positions?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationUserPositionPage(value)) {
    throw new Error('client.invalid_organization_user_position_page');
  }
  return value;
}

export async function createOrganizationUserPosition(
  userId: string,
  positionId: string,
  isPrimary = false
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>('/api/v1/organization/user-positions', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ userId, positionId, isPrimary })
  });
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }
  return value;
}

export async function updateOrganizationUserPosition(
  id: string,
  isPrimary: boolean,
  version: number
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/user-positions/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ isPrimary, version })
    }
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }
  return value;
}

export async function disableOrganizationUserPosition(
  id: string
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/user-positions/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }
  return value;
}
