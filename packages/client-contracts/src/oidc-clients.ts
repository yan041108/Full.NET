export interface CreateOidcClientRequest {
  clientId: string;
  displayName: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[] | null;
  scopes: string[];
  isConfidential: boolean;
  isFirstParty: boolean;
  resourceAudience: string | null;
}

export interface UpdateOidcClientRequest {
  displayName: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[] | null;
  scopes: string[];
  isFirstParty: boolean;
  resourceAudience: string | null;
  version: number;
}

export interface OidcClient {
  id: string;
  clientId: string;
  displayName: string;
  clientType: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  scopes: string[];
  isFirstParty: boolean;
  resourceAudience: string | null;
  isDisabled: boolean;
  createdAtUtc: string;
  version: number;
}

export interface CreateOidcClientResult {
  client: OidcClient;
  secret: string | null;
}

export interface RotateOidcClientSecretResult {
  client: OidcClient;
  secret: string;
}

export interface OidcClientPage {
  items: OidcClient[];
  page: number;
  pageSize: number;
  total: number;
}

export interface OidcClientListQuery {
  page?: number;
  pageSize?: number;
  clientIdContains?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isOidcClient(value: unknown): value is OidcClient {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.clientId === 'string'
    && typeof value.displayName === 'string'
    && typeof value.clientType === 'string'
    && Array.isArray(value.redirectUris)
    && value.redirectUris.every((uri) => typeof uri === 'string')
    && Array.isArray(value.postLogoutRedirectUris)
    && value.postLogoutRedirectUris.every((uri) => typeof uri === 'string')
    && Array.isArray(value.scopes)
    && value.scopes.every((scope) => typeof scope === 'string')
    && typeof value.isFirstParty === 'boolean'
    && (value.resourceAudience === null || typeof value.resourceAudience === 'string')
    && typeof value.isDisabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && Number.isInteger(value.version);
}

export function isCreateOidcClientResult(value: unknown): value is CreateOidcClientResult {
  return isRecord(value)
    && isOidcClient(value.client)
    && (value.secret === null || typeof value.secret === 'string');
}

export function isRotateOidcClientSecretResult(
  value: unknown
): value is RotateOidcClientSecretResult {
  return isRecord(value)
    && isOidcClient(value.client)
    && typeof value.secret === 'string'
    && value.secret.length > 0;
}

export function isOidcClientPage(value: unknown): value is OidcClientPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOidcClient)
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
