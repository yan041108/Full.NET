import {
  isOrganizationUnit,
  isOrganizationUnitPage,
  type OrganizationUnit,
  type OrganizationUnitPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listOrganizationUnits(
  page = 1,
  pageSize = 20
): Promise<OrganizationUnitPage> {
  const value = await request<unknown>(
    `/api/v1/organization/units?page=${page}&pageSize=${pageSize}`
  );
  if (!isOrganizationUnitPage(value)) {
    throw new Error('client.invalid_organization_unit_page');
  }
  return value;
}

export async function createOrganizationUnit(
  code: string,
  name: string,
  displayOrder = 10
): Promise<OrganizationUnit> {
  const value = await request<unknown>('/api/v1/organization/units', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      parentId: null,
      code,
      name,
      displayOrder
    })
  });
  if (!isOrganizationUnit(value)) throw new Error('client.invalid_organization_unit');
  return value;
}

export async function updateOrganizationUnit(
  id: string,
  name: string,
  displayOrder: number,
  version: number
): Promise<OrganizationUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/units/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        parentId: null,
        name,
        displayOrder,
        version
      })
    }
  );
  if (!isOrganizationUnit(value)) throw new Error('client.invalid_organization_unit');
  return value;
}

export async function disableOrganizationUnit(id: string): Promise<OrganizationUnit> {
  const value = await request<unknown>(
    `/api/v1/organization/units/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isOrganizationUnit(value)) throw new Error('client.invalid_organization_unit');
  return value;
}
