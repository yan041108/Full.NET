export interface CreateOpenAccessClientRequest {
  userId: string;
  name: string;
  description: string | null;
  remark: string | null;
  permissions: string[];
  expiresAtUtc: string | null;
}

export interface UpdateOpenAccessClientRequest {
  name: string;
  description: string | null;
  remark: string | null;
  permissions: string[];
  expiresAtUtc: string | null;
  version: number;
}

export interface OpenAccessClient {
  id: string;
  apiKeyId: string;
  userId: string;
  username: string;
  name: string;
  description: string | null;
  remark: string | null;
  accessKeyId: string;
  permissions: string[];
  expiresAtUtc: string | null;
  isActive: boolean;
  lastUsedAtUtc: string | null;
  createdAtUtc: string;
  version: number;
}

export interface CreateOpenAccessClientResult {
  client: OpenAccessClient;
  secret: string;
}

export interface OpenAccessClientPage {
  items: OpenAccessClient[];
  page: number;
  pageSize: number;
  total: number;
}

export interface OpenAccessClientListQuery {
  page?: number;
  pageSize?: number;
  userId?: string;
  nameContains?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isOpenAccessClient(value: unknown): value is OpenAccessClient {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.apiKeyId)
    && isGuid(value.userId)
    && typeof value.username === 'string'
    && typeof value.name === 'string'
    && (value.description === null || typeof value.description === 'string')
    && (value.remark === null || typeof value.remark === 'string')
    && typeof value.accessKeyId === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every((permission) => typeof permission === 'string')
    && (value.expiresAtUtc === null || typeof value.expiresAtUtc === 'string')
    && typeof value.isActive === 'boolean'
    && (value.lastUsedAtUtc === null || typeof value.lastUsedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && Number.isInteger(value.version);
}

export function isCreateOpenAccessClientResult(value: unknown): value is CreateOpenAccessClientResult {
  return isRecord(value)
    && isOpenAccessClient(value.client)
    && typeof value.secret === 'string'
    && value.secret.length > 0;
}

export function isOpenAccessClientPage(value: unknown): value is OpenAccessClientPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOpenAccessClient)
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
