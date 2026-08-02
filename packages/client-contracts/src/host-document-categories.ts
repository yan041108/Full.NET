export interface HostDocumentCategory {
  id: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateHostDocumentCategoryRequest {
  name: string;
  parentId?: string | null;
  sortOrder: number;
}

export interface UpdateHostDocumentCategoryRequest {
  name: string;
  parentId?: string | null;
  sortOrder: number;
  version: number;
}

export interface DeleteHostDocumentCategoryRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostDocumentCategory(value: unknown): value is HostDocumentCategory {
  return isRecord(value)
    && isGuid(value.id)
    && (value.parentId === null || isGuid(value.parentId))
    && isNonEmptyString(value.name)
    && Number.isInteger(value.sortOrder)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostDocumentCategoryList(value: unknown): value is HostDocumentCategory[] {
  return Array.isArray(value) && value.every(isHostDocumentCategory);
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
