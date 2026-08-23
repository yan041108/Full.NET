import {
  isOrganizationPosition,
  isOrganizationPositionPage,
  organizationAssignTenantPositionLevel,
  organizationAssignTenantPositionUnit,
  organizationCreateTenantPosition,
  organizationDisableTenantPosition,
  organizationListTenantPositions,
  organizationUpdateTenantPosition,
  type OrganizationPosition,
  type OrganizationPositionPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listOrganizationPositions(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationPositionPage> {
  const value = await organizationListTenantPositions(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationPositionPage(value)) {
    throw new Error('client.invalid_organization_position_page');
  }

  return value;
}

export async function createOrganizationPosition(
  code: string,
  name: string,
  displayOrder = 10,
  signal?: AbortSignal
): Promise<OrganizationPosition> {
  const value = await organizationCreateTenantPosition(
    http,
    { body: { code, name, displayOrder } },
    signal
  );
  if (!isOrganizationPosition(value)) {
    throw new Error('client.invalid_organization_position');
  }

  return value;
}

export async function updateOrganizationPosition(
  id: string,
  name: string,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationPosition> {
  const value = await organizationUpdateTenantPosition(
    http,
    { positionId: id, body: { name, displayOrder, version } },
    signal
  );
  if (!isOrganizationPosition(value)) {
    throw new Error('client.invalid_organization_position');
  }

  return value;
}

export async function assignOrganizationPositionUnit(
  id: string,
  unitId: string | null,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationPosition> {
  const value = await organizationAssignTenantPositionUnit(
    http,
    { positionId: id, body: { unitId, version } },
    signal
  );
  if (!isOrganizationPosition(value)) {
    throw new Error('client.invalid_organization_position');
  }

  return value;
}

export async function assignOrganizationPositionLevel(
  id: string,
  positionLevelId: string | null,
  version: number,
  signal?: AbortSignal
): Promise<OrganizationPosition> {
  const value = await organizationAssignTenantPositionLevel(
    http,
    { positionId: id, body: { positionLevelId, version } },
    signal
  );
  if (!isOrganizationPosition(value)) {
    throw new Error('client.invalid_organization_position');
  }

  return value;
}

export async function disableOrganizationPosition(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationPosition> {
  const value = await organizationDisableTenantPosition(
    http,
    { positionId: id },
    signal
  );
  if (!isOrganizationPosition(value)) {
    throw new Error('client.invalid_organization_position');
  }

  return value;
}
