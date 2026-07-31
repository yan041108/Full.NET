import {
  isOrganizationAssignableUserPage,
  isOrganizationUserUnit,
  isOrganizationUserUnitPage,
  type OrganizationAssignableUserPage,
  type OrganizationUserUnit,
  type OrganizationUserUnitPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listAssignableOrganizationUserUnitUsers(
  page = 1,
  pageSize = 100
): Promise<OrganizationAssignableUserPage> {
  const value = await request<unknown>(
    `/api/v1/organization/user-units/assignable-users?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationAssignableUserPage(value)) {
    throw new Error('client.invalid_organization_assignable_user_page');
  }
  return value;
}

export async function listOrganizationUserUnits(
  page = 1,
  pageSize = 20
): Promise<OrganizationUserUnitPage> {
  const value = await request<unknown>(
    `/api/v1/organization/user-units?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationUserUnitPage(value)) {
    throw new Error('client.invalid_organization_user_unit_page');
  }
  return value;
}

export async function createOrganizationUserUnit(
  userId: string,
  unitId: string,
  isPrimary = false
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>('/api/v1/organization/user-units', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ userId, unitId, isPrimary })
  });
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }
  return value;
}

export async function updateOrganizationUserUnit(
  id: string,
  isPrimary: boolean,
  version: number
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/user-units/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ isPrimary, version })
    }
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }
  return value;
}

export async function disableOrganizationUserUnit(
  id: string
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/user-units/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }
  return value;
}
