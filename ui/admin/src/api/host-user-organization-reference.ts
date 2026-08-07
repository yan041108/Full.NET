import {
  isHostUserOrganizationReference,
  isOrganizationUserPosition,
  isOrganizationUserUnit,
  type HostUserOrganizationReference,
  type OrganizationUserPosition,
  type OrganizationUserUnit
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getHostUserOrganizationReference(
  tenantId: string
): Promise<HostUserOrganizationReference> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/reference?tenantId=${encodeURIComponent(tenantId)}`
  );
  if (!isHostUserOrganizationReference(value)) {
    throw new Error('client.invalid_host_user_organization_reference');
  }

  return value;
}

export async function createHostUserOrganizationUnit(
  tenantId: string,
  userId: string,
  unitId: string,
  isPrimary = false
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-units?tenantId=${encodeURIComponent(tenantId)}`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ userId, unitId, isPrimary })
    }
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }
  return value;
}

export async function updateHostUserOrganizationUnit(
  tenantId: string,
  assignmentId: string,
  isPrimary: boolean,
  version: number
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(assignmentId)}?tenantId=${encodeURIComponent(tenantId)}`,
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

export async function disableHostUserOrganizationUnit(
  tenantId: string,
  assignmentId: string
): Promise<OrganizationUserUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-units/${encodeURIComponent(assignmentId)}/disable?tenantId=${encodeURIComponent(tenantId)}`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' }
  );
  if (!isOrganizationUserUnit(value)) {
    throw new Error('client.invalid_organization_user_unit');
  }
  return value;
}

export async function createHostUserOrganizationPosition(
  tenantId: string,
  userId: string,
  positionId: string,
  isPrimary = false
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-positions?tenantId=${encodeURIComponent(tenantId)}`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ userId, positionId, isPrimary })
    }
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }
  return value;
}

export async function updateHostUserOrganizationPosition(
  tenantId: string,
  assignmentId: string,
  isPrimary: boolean,
  version: number
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(assignmentId)}?tenantId=${encodeURIComponent(tenantId)}`,
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

export async function disableHostUserOrganizationPosition(
  tenantId: string,
  assignmentId: string
): Promise<OrganizationUserPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/host-user-management/user-positions/${encodeURIComponent(assignmentId)}/disable?tenantId=${encodeURIComponent(tenantId)}`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' }
  );
  if (!isOrganizationUserPosition(value)) {
    throw new Error('client.invalid_organization_user_position');
  }
  return value;
}
