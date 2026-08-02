export interface HostDocumentVersion {
  id: string;
  versionNumber: number;
  fileId: string;
  contentHash: string | null;
  sizeBytes: number;
  createdAtUtc: string;
  uploadedByUserId: string;
}

export interface HostDocumentItem {
  id: string;
  title: string;
  description: string | null;
  categoryId: string | null;
  currentVersion: HostDocumentVersion | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
}

export interface HostDocumentItemPage {
  items: HostDocumentItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostDocumentItemRequest {
  title: string;
  description?: string | null;
}

export interface UpdateHostDocumentItemRequest {
  title: string;
  description?: string | null;
  version: number;
}

export interface AddHostDocumentVersionRequest {
  fileId: string;
}

export interface DeleteHostDocumentItemRequest {
  version: number;
}

export interface RestoreHostDocumentItemRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostDocumentVersion(value: unknown): value is HostDocumentVersion {
  return isRecord(value)
    && isGuid(value.id)
    && Number.isInteger(value.versionNumber)
    && isGuid(value.fileId)
    && (value.contentHash === null || typeof value.contentHash === 'string')
    && Number.isInteger(value.sizeBytes)
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.uploadedByUserId);
}

export function isHostDocumentItem(value: unknown): value is HostDocumentItem {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.title)
    && (value.description === null || typeof value.description === 'string')
    && (value.categoryId === null || isGuid(value.categoryId))
    && (value.currentVersion === null || isHostDocumentVersion(value.currentVersion))
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && (value.updatedByUserId === null || isGuid(value.updatedByUserId))
    && Number.isInteger(value.version);
}

export function isHostDocumentItemPage(value: unknown): value is HostDocumentItemPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentItem)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
