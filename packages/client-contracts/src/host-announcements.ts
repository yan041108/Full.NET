export interface HostAnnouncement {
  id: string;
  title: string;
  content: string;
  status: 'draft' | 'published';
  publishedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface HostAnnouncementPage {
  items: HostAnnouncement[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateHostAnnouncementRequest {
  title: string;
  content: string;
}

export interface UpdateHostAnnouncementRequest {
  title: string;
  content: string;
  version: number;
}

export interface PublishHostAnnouncementRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostAnnouncement(value: unknown): value is HostAnnouncement {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && (value.status === 'draft' || value.status === 'published')
    && (value.publishedAtUtc === null || typeof value.publishedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostAnnouncementPage(
  value: unknown
): value is HostAnnouncementPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostAnnouncement)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isCreateHostAnnouncementRequest(
  value: unknown
): value is CreateHostAnnouncementRequest {
  return isRecord(value)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content);
}

export function isUpdateHostAnnouncementRequest(
  value: unknown
): value is UpdateHostAnnouncementRequest {
  return isRecord(value)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && Number.isInteger(value.version);
}

export function isPublishHostAnnouncementRequest(
  value: unknown
): value is PublishHostAnnouncementRequest {
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
