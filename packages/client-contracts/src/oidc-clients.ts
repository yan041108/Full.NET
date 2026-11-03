export interface CreateOidcClientRequest {
  clientId: string;
  displayName: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[] | null;
  scopes: string[];
  isConfidential: boolean;
  isFirstParty: boolean;
  resourceAudience: string | null;
  requireMfa?: boolean;
  backchannelLogoutUri?: string | null;
}

export interface UpdateOidcClientRequest {
  displayName: string;
  redirectUris: string[];
  postLogoutRedirectUris: string[] | null;
  scopes: string[];
  isFirstParty: boolean;
  resourceAudience: string | null;
  requireMfa?: boolean;
  backchannelLogoutUri?: string | null;
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
  requireMfa?: boolean;
  backchannelLogoutUri?: string | null;
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
    && (value.requireMfa === undefined || typeof value.requireMfa === 'boolean')
    && (value.backchannelLogoutUri === undefined || value.backchannelLogoutUri === null || typeof value.backchannelLogoutUri === 'string')
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

/** 有界的退出投递状态；不包含用户、令牌或接收端秘密。 */
export interface OidcLogoutDelivery {
  id: string;
  clientId: string;
  statusKey: 'pending' | 'delivered' | 'failed';
  attemptCount: number;
  nextAttemptAtUtc: string;
  lastErrorCode: string | null;
  createdAtUtc: string;
  deliveredAtUtc: string | null;
}

export function isOidcLogoutDelivery(value: unknown): value is OidcLogoutDelivery {
  return isRecord(value) && isGuid(value.id) && typeof value.clientId === 'string'
    && typeof value.statusKey === 'string' && ['pending', 'delivered', 'failed'].includes(value.statusKey)
    && typeof value.attemptCount === 'number' && Number.isInteger(value.attemptCount) && value.attemptCount >= 0
    && isTimestamp(value.nextAttemptAtUtc) && isTimestamp(value.createdAtUtc)
    && (value.lastErrorCode === null || typeof value.lastErrorCode === 'string')
    && (value.deliveredAtUtc === null || isTimestamp(value.deliveredAtUtc));
}

function isTimestamp(value: unknown): value is string {
  return typeof value === 'string' && Number.isFinite(Date.parse(value));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
