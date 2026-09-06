export interface OAuthProvider {
  id: string;
  providerKey: string;
  displayName: string;
  authority: string;
  clientId: string;
  scopes: string;
  redirectPath: string;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateOAuthProviderRequest {
  providerKey: string;
  displayName: string;
  authority: string;
  clientId: string;
  clientSecret: string;
  scopes?: string | null;
  redirectPath?: string | null;
  isEnabled: boolean;
}

export interface UpdateOAuthProviderRequest {
  displayName: string;
  authority: string;
  clientId: string;
  clientSecret?: string | null;
  scopes: string;
  redirectPath: string;
  isEnabled: boolean;
  version: number;
}

export interface PublicOAuthProvider {
  providerKey: string;
  displayName: string;
}

export interface OAuthProviderPage {
  items: OAuthProvider[];
  page: number;
  pageSize: number;
  total: number;
}

export interface OAuthProviderListQuery {
  page?: number;
  pageSize?: number;
  providerKeyContains?: string;
  displayNameContains?: string;
  isEnabled?: boolean;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isOAuthProvider(value: unknown): value is OAuthProvider {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.providerKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.authority === 'string'
    && typeof value.clientId === 'string'
    && typeof value.scopes === 'string'
    && typeof value.redirectPath === 'string'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isOAuthProviderPage(value: unknown): value is OAuthProviderPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOAuthProvider)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isPublicOAuthProvider(value: unknown): value is PublicOAuthProvider {
  return isRecord(value)
    && typeof value.providerKey === 'string'
    && typeof value.displayName === 'string';
}

export function isPublicOAuthProviderList(value: unknown): value is PublicOAuthProvider[] {
  return Array.isArray(value) && value.every(isPublicOAuthProvider);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
