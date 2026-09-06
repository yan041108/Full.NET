export type ReleaseNoteStatus = 'draft' | 'published' | 'retracted';

export interface HostReleaseNote {
  id: string;
  versionLabel: string;
  versionSortKey: number;
  title: string;
  content: string;
  status: ReleaseNoteStatus;
  publishedAtUtc: string | null;
  publishedByUserId: string | null;
  retractedAtUtc: string | null;
  retractedByUserId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostReleaseNotePage {
  items: HostReleaseNote[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostReleaseNoteRequest {
  versionLabel: string;
  title: string;
  content: string;
}

export interface UpdateHostReleaseNoteRequest {
  versionLabel: string;
  title: string;
  content: string;
  version: number;
}

export interface PublishHostReleaseNoteRequest {
  version: number;
}

export interface RetractHostReleaseNoteRequest {
  version: number;
}

export interface DeleteHostReleaseNoteRequest {
  version: number;
}

export interface HostReleaseNoteListQuery {
  page?: number;
  pageSize?: number;
  title?: string;
  status?: ReleaseNoteStatus | '';
  versionLabel?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostReleaseNote(value: unknown): value is HostReleaseNote {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.versionLabel)
    && typeof value.versionSortKey === 'number'
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && (value.status === 'draft' || value.status === 'published' || value.status === 'retracted')
    && (value.publishedAtUtc === null || typeof value.publishedAtUtc === 'string')
    && (value.publishedByUserId === null || isGuid(value.publishedByUserId))
    && (value.retractedAtUtc === null || typeof value.retractedAtUtc === 'string')
    && (value.retractedByUserId === null || isGuid(value.retractedByUserId))
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostReleaseNotePage(value: unknown): value is HostReleaseNotePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostReleaseNote)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isCreateHostReleaseNoteRequest(
  value: unknown
): value is CreateHostReleaseNoteRequest {
  return isRecord(value)
    && isNonEmptyString(value.versionLabel)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content);
}

export function isUpdateHostReleaseNoteRequest(
  value: unknown
): value is UpdateHostReleaseNoteRequest {
  return isRecord(value)
    && isNonEmptyString(value.versionLabel)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && Number.isInteger(value.version);
}

export function isPublishHostReleaseNoteRequest(
  value: unknown
): value is PublishHostReleaseNoteRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

export function isRetractHostReleaseNoteRequest(
  value: unknown
): value is RetractHostReleaseNoteRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

export function isDeleteHostReleaseNoteRequest(
  value: unknown
): value is DeleteHostReleaseNoteRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
