export interface HostDocumentTag {
  id: string;
  name: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateHostDocumentTagRequest {
  name: string;
}

export interface UpdateHostDocumentTagRequest {
  name: string;
  version: number;
}

export interface DeleteHostDocumentTagRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostDocumentTag(value: unknown): value is HostDocumentTag {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.name)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostDocumentTagList(value: unknown): value is HostDocumentTag[] {
  return Array.isArray(value) && value.every(isHostDocumentTag);
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
