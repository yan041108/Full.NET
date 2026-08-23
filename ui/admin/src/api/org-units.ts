import {
  isOrganizationUnit,
  isOrganizationUnitPage,
  organizationCreateTenantUnit,
  organizationDisableTenantUnit,
  organizationListTenantUnits,
  organizationUpdateTenantUnit,
  type OrganizationUnit,
  type OrganizationUnitPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listOrganizationUnits(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationUnitPage> {
  const value = await organizationListTenantUnits(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationUnitPage(value)) {
    throw new Error('client.invalid_organization_unit_page');
  }

  return value;
}

export async function createOrganizationUnit(
  code: string,
  name: string,
  displayOrder = 10,
  parentId: string | null = null,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationCreateTenantUnit(
    http,
    { body: { parentId, code, name, displayOrder } },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}

export async function updateOrganizationUnit(
  id: string,
  name: string,
  displayOrder: number,
  version: number,
  parentId: string | null = null,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationUpdateTenantUnit(
    http,
    { unitId: id, body: { parentId, name, displayOrder, version } },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}

export async function disableOrganizationUnit(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationUnit> {
  const value = await organizationDisableTenantUnit(
    http,
    { unitId: id },
    signal
  );
  if (!isOrganizationUnit(value)) {
    throw new Error('client.invalid_organization_unit');
  }

  return value;
}
