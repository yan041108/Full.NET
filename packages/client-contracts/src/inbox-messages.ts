export interface InboxMessage {
  id: string;
  title: string;
  content: string;
  status: 'unread' | 'read';
  readAtUtc: string | null;
  createdAtUtc: string;
  createdByUserId: string | null;
}

export interface InboxMessagePage {
  items: InboxMessage[];
  page: number;
  pageSize: number;
  total: number;
}

export interface InboxUnreadCount {
  unreadCount: number;
}

export interface SendHostInboxMessageRequest {
  recipientUserId: string;
  title: string;
  content: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isInboxMessage(value: unknown): value is InboxMessage {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content)
    && (value.status === 'unread' || value.status === 'read')
    && (value.readAtUtc === null || typeof value.readAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.createdByUserId === null || isGuid(value.createdByUserId));
}

export function isInboxMessagePage(value: unknown): value is InboxMessagePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isInboxMessage)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isInboxUnreadCount(value: unknown): value is InboxUnreadCount {
  return isRecord(value) && Number.isInteger(value.unreadCount);
}

export function isSendHostInboxMessageRequest(
  value: unknown
): value is SendHostInboxMessageRequest {
  return isRecord(value)
    && isGuid(value.recipientUserId)
    && isNonEmptyString(value.title)
    && isNonEmptyString(value.content);
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
