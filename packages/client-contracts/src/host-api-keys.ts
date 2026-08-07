export interface CreateHostApiKeyRequest {
  userId: string;
  displayName: string;
  permissions: string[];
  expiresAtUtc: string | null;
}

export interface HostApiKey {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  keyPrefix: string;
  permissions: string[];
  expiresAtUtc: string | null;
  isActive: boolean;
  lastUsedAtUtc: string | null;
  createdAtUtc: string;
}

export interface CreateHostApiKeyResult {
  key: HostApiKey;
  secret: string;
}

export interface HostApiKeyPage {
  items: HostApiKey[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostApiKey(value: unknown): value is HostApiKey {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.userId)
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && typeof value.keyPrefix === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every((permission) => typeof permission === 'string')
    && (value.expiresAtUtc === null || typeof value.expiresAtUtc === 'string')
    && typeof value.isActive === 'boolean'
    && (value.lastUsedAtUtc === null || typeof value.lastUsedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string';
}

export function isCreateHostApiKeyResult(value: unknown): value is CreateHostApiKeyResult {
  return isRecord(value)
    && isHostApiKey(value.key)
    && typeof value.secret === 'string'
    && value.secret.length > 0;
}

export function isHostApiKeyPage(value: unknown): value is HostApiKeyPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostApiKey)
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
