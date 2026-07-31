import {
  isOrganizationPositionLevel,
  isOrganizationPositionLevelPage,
  type OrganizationPositionLevel,
  type OrganizationPositionLevelPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listOrganizationPositionLevels(
  page = 1,
  pageSize = 20
): Promise<OrganizationPositionLevelPage> {
  const value = await request<unknown>(
    `/api/v1/organization/position-levels?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationPositionLevelPage(value)) {
    throw new Error('client.invalid_organization_position_level_page');
  }
  return value;
}

export async function createOrganizationPositionLevel(
  code: string,
  name: string,
  displayOrder = 10
): Promise<OrganizationPositionLevel> {
  const value = await request<unknown>(
    '/api/v1/organization/position-levels',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ code, name, displayOrder })
    }
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }
  return value;
}

export async function updateOrganizationPositionLevel(
  id: string,
  name: string,
  displayOrder: number,
  version: number
): Promise<OrganizationPositionLevel> {
  const value = await request<unknown>(
    `/api/v1/organization/position-levels/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, displayOrder, version })
    }
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }
  return value;
}

export async function disableOrganizationPositionLevel(
  id: string
): Promise<OrganizationPositionLevel> {
  const value = await request<unknown>(
    `/api/v1/organization/position-levels/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }
  return value;
}
