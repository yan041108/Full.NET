export interface HostDocumentCategoryResponse {
  id: string;
  parentId: string | null;
  name: string;
  code: string | null;
  sortOrder: number;
  icon: string | null;
  color: string | null;
  description: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateHostDocumentCategoryRequest {
  name: string;
  parentId?: string | null;
  sortOrder: number;
  code?: string | null;
  icon?: string | null;
  color?: string | null;
  description?: string | null;
}

export interface UpdateHostDocumentCategoryRequest {
  name: string;
  parentId?: string | null;
  sortOrder: number;
  code?: string | null;
  icon?: string | null;
  color?: string | null;
  description?: string | null;
  version: number;
}

export interface DeleteHostDocumentCategoryRequest {
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

export function isHostDocumentCategoryResponse(value: unknown): value is HostDocumentCategoryResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isNullableGuid(value.parentId)
    && isNonEmptyString(value.name)
    && isNullableString(value.code)
    && Number.isInteger(value.sortOrder)
    && isNullableString(value.icon)
    && isNullableString(value.color)
    && isNullableString(value.description)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostDocumentCategoryResponseList(value: unknown): value is HostDocumentCategoryResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentCategoryResponse);
}

export function isCreateHostDocumentCategoryRequest(value: unknown): value is CreateHostDocumentCategoryRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.parentId === undefined || isNullableGuid(value.parentId))
    && Number.isInteger(value.sortOrder)
    && (value.code === undefined || isNullableString(value.code))
    && (value.icon === undefined || isNullableString(value.icon))
    && (value.color === undefined || isNullableString(value.color))
    && (value.description === undefined || isNullableString(value.description));
}

export function isUpdateHostDocumentCategoryRequest(value: unknown): value is UpdateHostDocumentCategoryRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.parentId === undefined || isNullableGuid(value.parentId))
    && Number.isInteger(value.sortOrder)
    && (value.code === undefined || isNullableString(value.code))
    && (value.icon === undefined || isNullableString(value.icon))
    && (value.color === undefined || isNullableString(value.color))
    && (value.description === undefined || isNullableString(value.description))
    && Number.isInteger(value.version);
}

export function isDeleteHostDocumentCategoryRequest(value: unknown): value is DeleteHostDocumentCategoryRequest {
  return isRecord(value)
    && Number.isInteger(value.version);
}
