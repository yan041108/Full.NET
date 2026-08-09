export interface HostDocumentTagResponse {
  id: string;
  name: string;
  color: string | null;
  useCount: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateHostDocumentTagRequest {
  name: string;
  color?: string | null;
}

export interface UpdateHostDocumentTagRequest {
  name: string;
  color?: string | null;
  version: number;
}

export interface DeleteHostDocumentTagRequest {
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

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isHostDocumentTagResponse(value: unknown): value is HostDocumentTagResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.name)
    && isNullableString(value.color)
    && Number.isInteger(value.useCount)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostDocumentTagResponseList(value: unknown): value is HostDocumentTagResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentTagResponse);
}

export function isCreateHostDocumentTagRequest(value: unknown): value is CreateHostDocumentTagRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.color === undefined || isNullableString(value.color));
}

export function isUpdateHostDocumentTagRequest(value: unknown): value is UpdateHostDocumentTagRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.color === undefined || isNullableString(value.color))
    && Number.isInteger(value.version);
}

export function isDeleteHostDocumentTagRequest(value: unknown): value is DeleteHostDocumentTagRequest {
  return isRecord(value)
    && Number.isInteger(value.version);
}
