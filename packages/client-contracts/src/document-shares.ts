export interface CreateHostDocumentShareRequest {
  documentId: string;
  validDays: number;
  password?: string | null;
  maxAccessCount?: number | null;
}

export interface UpdateHostDocumentShareStatusRequest {
  isEnabled: boolean;
  version: number;
}

export interface HostDocumentShareResponse {
  id: string;
  documentId: string;
  shareCode: string;
  createdAtUtc: string;
  expireTime: string;
  maxAccessCount: number | null;
  accessCount: number;
  isEnabled: boolean;
  version: number;
  hasPassword: boolean;
}

export interface HostDocumentSharePage {
  items: HostDocumentShareResponse[];
  page: number;
  pageSize: number;
  total: number;
}

export interface AccessHostDocumentShareRequest {
  password?: string | null;
}

export interface HostDocumentShareAccessResponse {
  shareId: string;
  documentId: string;
  shareCode: string;
  title: string;
  fileName: string | null;
  mimeType: string | null;
  fileSizeBytes: number;
  hasPassword: boolean;
  accessCountRemaining: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function shareResponseHasForbiddenCredentialFields(value: Record<string, unknown>): boolean {
  return 'password' in value || 'passwordHash' in value;
}

export function isCreateHostDocumentShareRequest(value: unknown): value is CreateHostDocumentShareRequest {
  return isRecord(value)
    && isGuid(value.documentId)
    && Number.isInteger(value.validDays)
    && (value.password === undefined || isNullableString(value.password))
    && (value.maxAccessCount === undefined || value.maxAccessCount === null || Number.isInteger(value.maxAccessCount));
}

export function isUpdateHostDocumentShareStatusRequest(value: unknown): value is UpdateHostDocumentShareStatusRequest {
  return isRecord(value)
    && typeof value.isEnabled === 'boolean'
    && Number.isInteger(value.version);
}

export function isHostDocumentShareResponse(value: unknown): value is HostDocumentShareResponse {
  return isRecord(value)
    && !shareResponseHasForbiddenCredentialFields(value)
    && isGuid(value.id)
    && isGuid(value.documentId)
    && isNonEmptyString(value.shareCode)
    && typeof value.createdAtUtc === 'string'
    && typeof value.expireTime === 'string'
    && (value.maxAccessCount === null || Number.isInteger(value.maxAccessCount))
    && Number.isInteger(value.accessCount)
    && typeof value.isEnabled === 'boolean'
    && Number.isInteger(value.version)
    && typeof value.hasPassword === 'boolean';
}

export function isHostDocumentSharePage(value: unknown): value is HostDocumentSharePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentShareResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isAccessHostDocumentShareRequest(value: unknown): value is AccessHostDocumentShareRequest {
  return isRecord(value)
    && (value.password === undefined || isNullableString(value.password));
}

export function isHostDocumentShareAccessResponse(value: unknown): value is HostDocumentShareAccessResponse {
  return isRecord(value)
    && !shareResponseHasForbiddenCredentialFields(value)
    && isGuid(value.shareId)
    && isGuid(value.documentId)
    && isNonEmptyString(value.shareCode)
    && isNonEmptyString(value.title)
    && isNullableString(value.fileName)
    && isNullableString(value.mimeType)
    && Number.isInteger(value.fileSizeBytes)
    && typeof value.hasPassword === 'boolean'
    && Number.isInteger(value.accessCountRemaining);
}
