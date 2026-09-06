export interface CreateOpenAccessClientRequest {
  userId: string;
  name: string;
  description: string | null;
  remark: string | null;
  permissions: string[];
  expiresAtUtc: string | null;
  dailyRequestQuota?: number | null;
}

export interface UpdateOpenAccessClientRequest {
  name: string;
  description: string | null;
  remark: string | null;
  permissions: string[];
  expiresAtUtc: string | null;
  dailyRequestQuota: number | null;
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
  dailyRequestQuota: number | null;
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

export interface OpenAccessClientAccessLogEntry {
  id: string;
  eventType: string;
  resultCode: string;
  succeeded: boolean;
  ipAddress: string | null;
  userAgent: string | null;
  occurredAtUtc: string;
}

export interface OpenAccessClientAccessLogPage {
  items: OpenAccessClientAccessLogEntry[];
  page: number;
  pageSize: number;
  total: number;
}

export interface OpenAccessClientAccessLogQuery {
  page?: number;
  pageSize?: number;
  succeeded?: boolean;
  fromUtc?: string;
  toUtc?: string;
}

export interface OpenAccessClientUsage {
  clientId: string;
  dailyRequestQuota: number | null;
  todaySuccessCount: number;
  todayFailureCount: number;
  windowStartUtc: string;
  windowEndUtc: string;
  quotaExceeded: boolean;
}

export interface OpenAccessClientSignatureDebugRequest {
  secret: string;
  method: string;
  path: string;
  query: string | null;
  bodyBase64: string | null;
  timestamp: string;
  nonce: string;
  signature: string;
  signatureVersion: string;
}

export interface OpenAccessClientSignatureDebugResult {
  signaturesMatch: boolean;
  canonicalString: string;
  contentHash: string;
  expectedSignature: string;
  providedSignature: string;
  diagnostics: string[];
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
    && (value.dailyRequestQuota === null || Number.isInteger(value.dailyRequestQuota))
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

export function isOpenAccessClientAccessLogEntry(value: unknown): value is OpenAccessClientAccessLogEntry {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.eventType === 'string'
    && typeof value.resultCode === 'string'
    && typeof value.succeeded === 'boolean'
    && (value.ipAddress === null || typeof value.ipAddress === 'string')
    && (value.userAgent === null || typeof value.userAgent === 'string')
    && typeof value.occurredAtUtc === 'string';
}

export function isOpenAccessClientAccessLogPage(value: unknown): value is OpenAccessClientAccessLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isOpenAccessClientAccessLogEntry)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isOpenAccessClientUsage(value: unknown): value is OpenAccessClientUsage {
  return isRecord(value)
    && isGuid(value.clientId)
    && (value.dailyRequestQuota === null || Number.isInteger(value.dailyRequestQuota))
    && Number.isInteger(value.todaySuccessCount)
    && Number.isInteger(value.todayFailureCount)
    && typeof value.windowStartUtc === 'string'
    && typeof value.windowEndUtc === 'string'
    && typeof value.quotaExceeded === 'boolean';
}

export function isOpenAccessClientSignatureDebugResult(
  value: unknown
): value is OpenAccessClientSignatureDebugResult {
  return isRecord(value)
    && typeof value.signaturesMatch === 'boolean'
    && typeof value.canonicalString === 'string'
    && typeof value.contentHash === 'string'
    && typeof value.expectedSignature === 'string'
    && typeof value.providedSignature === 'string'
    && Array.isArray(value.diagnostics)
    && value.diagnostics.every((item) => typeof item === 'string');
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
