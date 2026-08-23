import {
  isOrganizationPositionLevel,
  isOrganizationPositionLevelPage,
  organizationCreateTenantPositionLevel,
  organizationDisableTenantPositionLevel,
  organizationListTenantPositionLevels,
  organizationUpdateTenantPositionLevel,
  type OrganizationPositionLevel,
  type OrganizationPositionLevelPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listOrganizationPositionLevels(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<OrganizationPositionLevelPage> {
  const value = await organizationListTenantPositionLevels(
    http,
    { page, pageSize },
    signal
  );
  if (!isOrganizationPositionLevelPage(value)) {
    throw new Error('client.invalid_organization_position_level_page');
  }

  return value;
}

export async function createOrganizationPositionLevel(
  code: string,
  name: string,
  displayOrder = 10,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationCreateTenantPositionLevel(
    http,
    { body: { code, name, displayOrder } },
    signal
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
  version: number,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationUpdateTenantPositionLevel(
    http,
    { positionLevelId: id, body: { name, displayOrder, version } },
    signal
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }

  return value;
}

export async function disableOrganizationPositionLevel(
  id: string,
  signal?: AbortSignal
): Promise<OrganizationPositionLevel> {
  const value = await organizationDisableTenantPositionLevel(
    http,
    { positionLevelId: id },
    signal
  );
  if (!isOrganizationPositionLevel(value)) {
    throw new Error('client.invalid_organization_position_level');
  }

  return value;
}
