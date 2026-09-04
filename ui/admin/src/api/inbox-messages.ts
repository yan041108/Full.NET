import {
  isInboxMessage,
  isInboxMessagePage,
  isInboxUnreadCount,
  notificationsGetMyInboxUnreadCount,
  notificationsMarkAllMyInboxMessagesRead,
  notificationsMarkMyInboxMessageRead,
  notificationsSendHostInboxMessage,
  type InboxMessage,
  type InboxMessagePage,
  type InboxUnreadCount
} from '@fullnet/client-contracts';
import { http, request } from './http';

export interface InboxMessageListQuery {
  page?: number;
  pageSize?: number;
  title?: string;
  status?: 'read' | 'unread' | '';
}

function buildListQuery(query: InboxMessageListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.title?.trim()) {
    params.set('title', query.title.trim());
  }
  if (query.status) {
    params.set('status', query.status);
  }
  return params.toString();
}

/** 分页查询当前用户站内信列表，并对响应页做失败关闭校验。 */
export async function listInboxMessages(
  query: InboxMessageListQuery = {},
  signal?: AbortSignal
): Promise<InboxMessagePage> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-inbox-messages?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isInboxMessagePage(value)) {
    throw new Error('client.invalid_inbox_message_page');
  }

  return value;
}

/** 查询当前用户未读站内信数量。 */
export async function getInboxUnreadCount(
  signal?: AbortSignal
): Promise<InboxUnreadCount> {
  const value = await notificationsGetMyInboxUnreadCount(http, {}, signal);
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }

  return value;
}

/** 将单条站内信标记为已读。 */
export async function markInboxMessageRead(
  id: string,
  signal?: AbortSignal
): Promise<InboxMessage> {
  const value = await notificationsMarkMyInboxMessageRead(
    http,
    { messageId: id },
    signal
  );
  if (!isInboxMessage(value)) {
    throw new Error('client.invalid_inbox_message');
  }

  return value;
}

/** 将当前用户全部站内信标记为已读。 */
export async function markAllInboxMessagesRead(
  signal?: AbortSignal
): Promise<InboxUnreadCount> {
  const value = await notificationsMarkAllMyInboxMessagesRead(http, {}, signal);
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }

  return value;
}

/** 由 Host 向指定用户发送站内信。 */
export async function sendHostInboxMessage(
  recipientUserId: string,
  title: string,
  content: string,
  signal?: AbortSignal
): Promise<InboxMessage> {
  const value = await notificationsSendHostInboxMessage(
    http,
    { body: { recipientUserId, title, content } },
    signal
  );
  if (!isInboxMessage(value)) {
    throw new Error('client.invalid_inbox_message');
  }

  return value;
}

/** 导出站内信列表、明细与未读数模型，供收件箱页和实时未读提醒共享同一契约。 */
export type { InboxMessage, InboxMessagePage, InboxUnreadCount };
