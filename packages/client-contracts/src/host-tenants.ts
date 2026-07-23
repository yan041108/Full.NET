export interface HostTenant {
  id: string;
  identifier: string;
  name: string;
  domain: string;
  isActive: boolean;
  version: number;
  defaultLocale: string;
}

export interface HostTenantPage {
  items: HostTenant[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostTenantRequest {
  identifier: string;
  name: string;
  domain: string;
}

export interface UpdateHostTenantRequest {
  name: string;
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const identifierPattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

export function isHostTenant(value: unknown): value is HostTenant {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.identifier === 'string'
    && identifierPattern.test(value.identifier)
    && isNonEmptyString(value.name)
    && isNonEmptyString(value.domain)
    && typeof value.isActive === 'boolean'
    && typeof value.version === 'number'
    && Number.isInteger(value.version)
    && typeof value.defaultLocale === 'string'
    && value.defaultLocale.length > 0;
}

export function isHostTenantPage(value: unknown): value is HostTenantPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostTenant)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isCreateHostTenantRequest(
  value: unknown
): value is CreateHostTenantRequest {
  return isRecord(value)
    && typeof value.identifier === 'string'
    && identifierPattern.test(value.identifier)
    && isNonEmptyString(value.name)
    && isNonEmptyString(value.domain);
}

export function isUpdateHostTenantRequest(
  value: unknown
): value is UpdateHostTenantRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
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
