export interface RegistrationPolicy {
  id: string;
  isPublicRegistrationEnabled: boolean;
  updatedAtUtc: string;
  version: number;
}

export interface UpdateRegistrationPolicyRequest {
  isPublicRegistrationEnabled: boolean;
  version: number;
}

export interface RegistrationWay {
  id: string;
  tenantId: string;
  name: string;
  code: string;
  isEnabled: boolean;
  roleId: string;
  organizationUnitId: string;
  positionId: string | null;
  sortOrder: number;
  remark: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateRegistrationWayRequest {
  tenantId: string;
  name: string;
  code: string;
  isEnabled: boolean;
  roleId: string;
  organizationUnitId: string;
  positionId?: string | null;
  sortOrder: number;
  remark?: string | null;
}

export interface UpdateRegistrationWayRequest {
  name: string;
  code: string;
  isEnabled: boolean;
  roleId: string;
  organizationUnitId: string;
  positionId?: string | null;
  sortOrder: number;
  remark?: string | null;
  version: number;
}

export interface RegistrationWayPage {
  items: RegistrationWay[];
  page: number;
  pageSize: number;
  total: number;
}

export interface RegistrationWayListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  nameContains?: string;
  isEnabled?: boolean;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isRegistrationPolicy(value: unknown): value is RegistrationPolicy {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.isPublicRegistrationEnabled === 'boolean'
    && typeof value.updatedAtUtc === 'string'
    && Number.isInteger(value.version);
}

export function isRegistrationWay(value: unknown): value is RegistrationWay {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && typeof value.name === 'string'
    && typeof value.code === 'string'
    && typeof value.isEnabled === 'boolean'
    && isGuid(value.roleId)
    && isGuid(value.organizationUnitId)
    && (value.positionId === null || isGuid(value.positionId))
    && Number.isInteger(value.sortOrder)
    && (value.remark === null || typeof value.remark === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isRegistrationWayPage(value: unknown): value is RegistrationWayPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isRegistrationWay)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
