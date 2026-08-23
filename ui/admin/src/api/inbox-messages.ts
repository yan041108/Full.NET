import {
  isInboxMessage,
  isInboxMessagePage,
  isInboxUnreadCount,
  notificationsGetMyInboxUnreadCount,
  notificationsListMyInboxMessages,
  notificationsMarkAllMyInboxMessagesRead,
  notificationsMarkMyInboxMessageRead,
  notificationsSendHostInboxMessage,
  type InboxMessage,
  type InboxMessagePage,
  type InboxUnreadCount
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listInboxMessages(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<InboxMessagePage> {
  const value = await notificationsListMyInboxMessages(
    http,
    { page, pageSize },
    signal
  );
  if (!isInboxMessagePage(value)) {
    throw new Error('client.invalid_inbox_message_page');
  }

  return value;
}

export async function getInboxUnreadCount(
  signal?: AbortSignal
): Promise<InboxUnreadCount> {
  const value = await notificationsGetMyInboxUnreadCount(http, {}, signal);
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }

  return value;
}

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

export async function markAllInboxMessagesRead(
  signal?: AbortSignal
): Promise<InboxUnreadCount> {
  const value = await notificationsMarkAllMyInboxMessagesRead(http, {}, signal);
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }

  return value;
}

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

export type { InboxMessage, InboxMessagePage, InboxUnreadCount };
