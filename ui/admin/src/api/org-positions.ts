import {
  isOrganizationPosition,
  isOrganizationPositionPage,
  type OrganizationPosition,
  type OrganizationPositionPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listOrganizationPositions(
  page = 1,
  pageSize = 20
): Promise<OrganizationPositionPage> {
  const value = await request<unknown>(
    `/api/v1/organization/positions?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationPositionPage(value)) {
    throw new Error('client.invalid_organization_position_page');
  }
  return value;
}

export async function createOrganizationPosition(
  code: string,
  name: string,
  displayOrder = 10
): Promise<OrganizationPosition> {
  const value = await request<unknown>('/api/v1/organization/positions', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ code, name, displayOrder })
  });
  if (!isOrganizationPosition(value)) throw new Error('client.invalid_organization_position');
  return value;
}

export async function updateOrganizationPosition(
  id: string,
  name: string,
  displayOrder: number,
  version: number
): Promise<OrganizationPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/positions/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, displayOrder, version })
    }
  );
  if (!isOrganizationPosition(value)) throw new Error('client.invalid_organization_position');
  return value;
}

export async function disableOrganizationPosition(id: string): Promise<OrganizationPosition> {
  const value = await request<unknown>(
    `/api/v1/organization/positions/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isOrganizationPosition(value)) throw new Error('client.invalid_organization_position');
  return value;
}
