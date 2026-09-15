export interface OidcAuthorization {
  id: string;
  applicationId: string | null;
  clientId: string | null;
  subject: string | null;
  scopes: string[];
  status: string;
  type: string;
  creationDateUtc: string | null;
  createdAtUtc: string;
  version: number;
}

export interface OidcAuthorizationPage {
  items: OidcAuthorization[];
  page: number;
  pageSize: number;
  total: number;
}

export interface OidcAuthorizationListQuery {
  page?: number;
  pageSize?: number;
  applicationId?: string;
  subject?: string;
  status?: string;
  clientIdContains?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isOidcAuthorization(value: unknown): value is OidcAuthorization {
  return isRecord(value)
    && isGuid(value.id)
    && (value.applicationId === null || isGuid(value.applicationId))
    && (value.clientId === null || typeof value.clientId === 'string')
    && (value.subject === null || typeof value.subject === 'string')
    && Array.isArray(value.scopes)
    && value.scopes.every((scope) => typeof scope === 'string')
    && typeof value.status === 'string'
    && typeof value.type === 'string'
    && (value.creationDateUtc === null || typeof value.creationDateUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && Number.isInteger(value.version);
}

export function isOidcAuthorizationPage(value: unknown): value is OidcAuthorizationPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOidcAuthorization)
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
