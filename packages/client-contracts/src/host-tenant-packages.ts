export interface HostTenantPackage {
  id: string;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  version: number;
  assignedTenantCount: number;
}

export interface HostTenantPackagePage {
  items: HostTenantPackage[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostTenantPackageRequest {
  code: string;
  name: string;
  description?: string | null;
}

export interface UpdateHostTenantPackageRequest {
  name: string;
  description?: string | null;
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const codePattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

export function isHostTenantPackage(value: unknown): value is HostTenantPackage {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.code === 'string'
    && codePattern.test(value.code)
    && isNonEmptyString(value.name)
    && (value.description === null || typeof value.description === 'string')
    && typeof value.isActive === 'boolean'
    && typeof value.version === 'number'
    && Number.isInteger(value.version)
    && typeof value.assignedTenantCount === 'number'
    && Number.isInteger(value.assignedTenantCount)
    && value.assignedTenantCount >= 0;
}

export function isHostTenantPackagePage(
  value: unknown
): value is HostTenantPackagePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostTenantPackage)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isCreateHostTenantPackageRequest(
  value: unknown
): value is CreateHostTenantPackageRequest {
  return isRecord(value)
    && typeof value.code === 'string'
    && codePattern.test(value.code)
    && isNonEmptyString(value.name)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string');
}

export function isUpdateHostTenantPackageRequest(
  value: unknown
): value is UpdateHostTenantPackageRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
