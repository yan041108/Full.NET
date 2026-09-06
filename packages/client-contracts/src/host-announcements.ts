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

export interface ReceivedHostAnnouncementListItem {
  id: string;
  title: string;
  kind: AnnouncementKind;
  audienceKind: AnnouncementAudienceKind;
  publishedAtUtc: string;
  isRead: boolean;
  readAtUtc: string | null;
}

export interface ReceivedHostAnnouncementDetail extends ReceivedHostAnnouncementListItem {
  content: string;
  publishedByUserId: string | null;
}

export interface ReceivedHostAnnouncementPage {
  items: ReceivedHostAnnouncementListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface HostAnnouncementUnreadCount {
  unreadCount: number;
}

export interface HostAnnouncementReadStats {
  eligibleRecipientCount: number;
  readCount: number;
  unreadCount: number;
}

export interface HostAnnouncementReadReceipt {
  userId: string;
  username: string | null;
  displayName: string | null;
  readAtUtc: string;
}

export interface HostAnnouncementReadReceiptPage {
  items: HostAnnouncementReadReceipt[];
  page: number;
  pageSize: number;
  total: number;
}

export interface ReceivedHostAnnouncementListQuery {
  page?: number;
  pageSize?: number;
  title?: string;
  isRead?: boolean;
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

export function isReceivedHostAnnouncementListItem(
  value: unknown
): value is ReceivedHostAnnouncementListItem {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.title)
    && (value.kind === 'notice' || value.kind === 'announcement')
    && (value.audienceKind === 'all' || value.audienceKind === 'users' || value.audienceKind === 'organizations')
    && typeof value.publishedAtUtc === 'string'
    && typeof value.isRead === 'boolean'
    && (value.readAtUtc === null || typeof value.readAtUtc === 'string');
}

export function isReceivedHostAnnouncementDetail(
  value: unknown
): value is ReceivedHostAnnouncementDetail {
  return isReceivedHostAnnouncementListItem(value)
    && isNonEmptyString((value as ReceivedHostAnnouncementDetail).content)
    && ((value as ReceivedHostAnnouncementDetail).publishedByUserId === null
      || isGuid((value as ReceivedHostAnnouncementDetail).publishedByUserId));
}

export function isReceivedHostAnnouncementPage(
  value: unknown
): value is ReceivedHostAnnouncementPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isReceivedHostAnnouncementListItem)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isHostAnnouncementUnreadCount(
  value: unknown
): value is HostAnnouncementUnreadCount {
  return isRecord(value) && Number.isInteger(value.unreadCount);
}

export function isHostAnnouncementReadStats(
  value: unknown
): value is HostAnnouncementReadStats {
  return isRecord(value)
    && Number.isInteger(value.eligibleRecipientCount)
    && Number.isInteger(value.readCount)
    && Number.isInteger(value.unreadCount);
}

export function isHostAnnouncementReadReceiptPage(
  value: unknown
): value is HostAnnouncementReadReceiptPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every((item) => isRecord(item)
      && isGuid(item.userId)
      && (item.username === null || typeof item.username === 'string')
      && (item.displayName === null || typeof item.displayName === 'string')
      && typeof item.readAtUtc === 'string')
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
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
