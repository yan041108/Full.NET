export type HostDocumentType = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 99;

export const HOST_DOCUMENT_TYPES = {
  Word: 1 as HostDocumentType,
  Excel: 2 as HostDocumentType,
  Pdf: 3 as HostDocumentType,
  Ppt: 4 as HostDocumentType,
  Txt: 5 as HostDocumentType,
  Video: 6 as HostDocumentType,
  Audio: 7 as HostDocumentType,
  Image: 8 as HostDocumentType,
  Zip: 9 as HostDocumentType,
  Rar: 10 as HostDocumentType,
  Other: 99 as HostDocumentType,
} as const;

export type HostDocumentStatus = 1 | 2 | 3 | 4;

export const HOST_DOCUMENT_STATUSES = {
  Draft: 1 as HostDocumentStatus,
  Published: 2 as HostDocumentStatus,
  Archived: 3 as HostDocumentStatus,
  Deleted: 4 as HostDocumentStatus,
} as const;

export interface HostDocumentVersionResponse {
  id: string;
  versionNumber: number;
  fileId: string;
  contentHash: string | null;
  sizeBytes: number;
  changeDescription: string | null;
  createdAtUtc: string;
  uploadedByUserId: string;
}

export interface HostDocumentTagAssignmentResponse {
  tagId: string;
  name: string;
  color: string | null;
}

export interface HostDocumentItemResponse {
  id: string;
  documentNo: string;
  title: string;
  description: string | null;
  categoryId: string | null;
  categoryName: string | null;
  categoryColor: string | null;
  documentType: number;
  sizeKb: number;
  thumbnail: string | null;
  status: number;
  accessCount: number;
  sort: number;
  lastAccessTime: string | null;
  currentVersion: HostDocumentVersionResponse | null;
  tags: HostDocumentTagAssignmentResponse[];
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  deletedAtUtc: string | null;
  deletedByUserId: string | null;
  version: number;
}

export interface HostDocumentItemPage {
  items: HostDocumentItemResponse[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostDocumentItemRequest {
  title: string;
  description?: string | null;
  categoryId?: string | null;
  documentType: number;
  thumbnail?: string | null;
  status: number;
  sort: number;
  tagIds?: string[];
}

export interface UpdateHostDocumentItemRequest {
  title: string;
  description?: string | null;
  categoryId?: string | null;
  thumbnail?: string | null;
  status?: number | null;
  sort?: number | null;
  tagIds?: string[] | null;
  version: number;
}

export interface AddHostDocumentVersionRequest {
  fileId: string;
  changeDescription?: string | null;
}

export interface DeleteHostDocumentItemRequest {
  version: number;
}

export interface RestoreHostDocumentItemRequest {
  version: number;
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

function isNullableGuid(value: unknown): value is string | null {
  return value === null || isGuid(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isHostDocumentType(value: unknown): value is HostDocumentType {
  return typeof value === 'number' && (
    (value >= 1 && value <= 10) || value === 99
  );
}

export function isHostDocumentStatus(value: unknown): value is HostDocumentStatus {
  return typeof value === 'number' && value >= 1 && value <= 4;
}

export function isHostDocumentVersionResponse(value: unknown): value is HostDocumentVersionResponse {
  return isRecord(value)
    && isGuid(value.id)
    && Number.isInteger(value.versionNumber)
    && isGuid(value.fileId)
    && isNullableString(value.contentHash)
    && Number.isInteger(value.sizeBytes)
    && isNullableString(value.changeDescription)
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.uploadedByUserId);
}

export function isHostDocumentVersionList(value: unknown): value is HostDocumentVersionResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentVersionResponse);
}

export function isHostDocumentTagAssignmentResponse(value: unknown): value is HostDocumentTagAssignmentResponse {
  return isRecord(value)
    && isGuid(value.tagId)
    && isNonEmptyString(value.name)
    && isNullableString(value.color);
}

export function isHostDocumentItemResponse(value: unknown): value is HostDocumentItemResponse {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.documentNo === 'string'
    && isNonEmptyString(value.title)
    && isNullableString(value.description)
    && isNullableGuid(value.categoryId)
    && isNullableString(value.categoryName)
    && isNullableString(value.categoryColor)
    && Number.isInteger(value.documentType)
    && typeof value.sizeKb === 'number'
    && isNullableString(value.thumbnail)
    && Number.isInteger(value.status)
    && Number.isInteger(value.accessCount)
    && Number.isInteger(value.sort)
    && (value.lastAccessTime === null || typeof value.lastAccessTime === 'string')
    && (value.currentVersion === null || isHostDocumentVersionResponse(value.currentVersion))
    && Array.isArray(value.tags) && value.tags.every(isHostDocumentTagAssignmentResponse)
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && isNullableGuid(value.updatedByUserId)
    && (value.deletedAtUtc === null || typeof value.deletedAtUtc === 'string')
    && isNullableGuid(value.deletedByUserId)
    && Number.isInteger(value.version);
}

export function isHostDocumentItemPage(value: unknown): value is HostDocumentItemPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentItemResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isCreateHostDocumentItemRequest(value: unknown): value is CreateHostDocumentItemRequest {
  return isRecord(value)
    && isNonEmptyString(value.title)
    && (value.description === undefined || isNullableString(value.description))
    && (value.categoryId === undefined || isNullableGuid(value.categoryId))
    && Number.isInteger(value.documentType)
    && (value.thumbnail === undefined || isNullableString(value.thumbnail))
    && Number.isInteger(value.status)
    && Number.isInteger(value.sort)
    && (value.tagIds === undefined || (Array.isArray(value.tagIds) && value.tagIds.every(isGuid)));
}

export function isUpdateHostDocumentItemRequest(value: unknown): value is UpdateHostDocumentItemRequest {
  return isRecord(value)
    && isNonEmptyString(value.title)
    && (value.description === undefined || isNullableString(value.description))
    && (value.categoryId === undefined || isNullableGuid(value.categoryId))
    && (value.thumbnail === undefined || isNullableString(value.thumbnail))
    && (value.status === undefined || value.status === null || Number.isInteger(value.status))
    && (value.sort === undefined || value.sort === null || Number.isInteger(value.sort))
    && (value.tagIds === undefined || value.tagIds === null || (Array.isArray(value.tagIds) && value.tagIds.every(isGuid)))
    && Number.isInteger(value.version);
}

export function isAddHostDocumentVersionRequest(value: unknown): value is AddHostDocumentVersionRequest {
  return isRecord(value)
    && isGuid(value.fileId)
    && (value.changeDescription === undefined || isNullableString(value.changeDescription));
}

export function isDeleteHostDocumentItemRequest(value: unknown): value is DeleteHostDocumentItemRequest {
  return isRecord(value)
    && Number.isInteger(value.version);
}

export function isRestoreHostDocumentItemRequest(value: unknown): value is RestoreHostDocumentItemRequest {
  return isRecord(value)
    && Number.isInteger(value.version);
}
