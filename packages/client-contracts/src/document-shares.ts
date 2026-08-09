export type HostDocumentSharePermission = 1 | 2;

export const HOST_DOCUMENT_SHARE_PERMISSIONS = {
  View: 1 as HostDocumentSharePermission,
  Download: 2 as HostDocumentSharePermission,
} as const;

export interface CreateHostDocumentShareRequest {
  documentId: string;
  password?: string | null;
  validDays: number;
  sharePermission: number;
}

export interface UpdateHostDocumentShareStatusRequest {
  isEnabled: boolean;
}

export interface HostDocumentShareResponse {
  id: string;
  documentId: string;
  documentNo: string;
  documentTitle: string;
  categoryName: string | null;
  shareCode: string;
  shareUrl: string;
  hasPassword: boolean;
  validDays: number;
  expireTime: string | null;
  accessCount: number;
  sharePermission: number;
  isEnabled: boolean;
  createdAtUtc: string;
  createdByUserId: string;
}

export interface HostDocumentSharePage {
  items: HostDocumentShareResponse[];
  page: number;
  pageSize: number;
  total: number;
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

export function isHostDocumentSharePermission(value: unknown): value is HostDocumentSharePermission {
  return typeof value === 'number' && (value === 1 || value === 2);
}

export function isCreateHostDocumentShareRequest(value: unknown): value is CreateHostDocumentShareRequest {
  return isRecord(value)
    && isGuid(value.documentId)
    && (value.password === undefined || isNullableString(value.password))
    && Number.isInteger(value.validDays)
    && Number.isInteger(value.sharePermission);
}

export function isUpdateHostDocumentShareStatusRequest(value: unknown): value is UpdateHostDocumentShareStatusRequest {
  return isRecord(value)
    && typeof value.isEnabled === 'boolean';
}

export function isHostDocumentShareResponse(value: unknown): value is HostDocumentShareResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentId)
    && typeof value.documentNo === 'string'
    && isNonEmptyString(value.documentTitle)
    && isNullableString(value.categoryName)
    && isNonEmptyString(value.shareCode)
    && isNonEmptyString(value.shareUrl)
    && typeof value.hasPassword === 'boolean'
    && Number.isInteger(value.validDays)
    && (value.expireTime === null || typeof value.expireTime === 'string')
    && Number.isInteger(value.accessCount)
    && Number.isInteger(value.sharePermission)
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId);
}

export function isHostDocumentSharePage(value: unknown): value is HostDocumentSharePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentShareResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}
