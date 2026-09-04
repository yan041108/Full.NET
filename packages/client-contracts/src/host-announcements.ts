export type AnnouncementKind = 'notice' | 'announcement';
export type AnnouncementAudienceKind = 'all' | 'users' | 'organizations';
export type AnnouncementStatus = 'draft' | 'published' | 'retracted';

export interface HostAnnouncementTargetOrganization {
  tenantId: string;
  organizationUnitId: string;
}

export interface HostAnnouncement {
  id: string;
  title: string;
  content: string;
  kind: AnnouncementKind;
  audienceKind: AnnouncementAudienceKind;
  status: AnnouncementStatus;
  publishedAtUtc: string | null;
  publishedByUserId: string | null;
  retractedAtUtc: string | null;
  retractedByUserId: string | null;
  targetUserIds: string[];
  targetOrganizations: HostAnnouncementTargetOrganization[];
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
  kind?: AnnouncementKind;
  audienceKind?: AnnouncementAudienceKind;
  targetUserIds?: string[];
  targetOrganizations?: HostAnnouncementTargetOrganization[];
}

export interface UpdateHostAnnouncementRequest {
  title: string;
  content: string;
  version: number;
  kind?: AnnouncementKind;
  audienceKind?: AnnouncementAudienceKind;
  targetUserIds?: string[];
  targetOrganizations?: HostAnnouncementTargetOrganization[];
}

export interface PublishHostAnnouncementRequest {
  version: number;
}

export interface RetractHostAnnouncementRequest {
  version: number;
}

export interface HostAnnouncementListQuery {
  page?: number;
  pageSize?: number;
  title?: string;
  status?: AnnouncementStatus | '';
  kind?: AnnouncementKind | '';
  audienceKind?: AnnouncementAudienceKind | '';
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isHostAnnouncement(value: unknown): value is HostAnnouncement {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && (value.kind === 'notice' || value.kind === 'announcement')
    && (value.audienceKind === 'all' || value.audienceKind === 'users' || value.audienceKind === 'organizations')
    && (value.status === 'draft' || value.status === 'published' || value.status === 'retracted')
    && (value.publishedAtUtc === null || typeof value.publishedAtUtc === 'string')
    && (value.publishedByUserId === null || isGuid(value.publishedByUserId))
    && (value.retractedAtUtc === null || typeof value.retractedAtUtc === 'string')
    && (value.retractedByUserId === null || isGuid(value.retractedByUserId))
    && Array.isArray(value.targetUserIds)
    && value.targetUserIds.every(isGuid)
    && Array.isArray(value.targetOrganizations)
    && value.targetOrganizations.every(isHostAnnouncementTargetOrganization)
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

export function isRetractHostAnnouncementRequest(
  value: unknown
): value is RetractHostAnnouncementRequest {
  return isRecord(value) && Number.isInteger(value.version);
}

function isHostAnnouncementTargetOrganization(
  value: unknown
): value is HostAnnouncementTargetOrganization {
  return isRecord(value)
    && isGuid(value.tenantId)
    && isGuid(value.organizationUnitId);
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
