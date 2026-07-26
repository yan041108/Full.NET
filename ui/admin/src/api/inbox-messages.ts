import {
  isInboxMessage,
  isInboxMessagePage,
  isInboxUnreadCount,
  type InboxMessage,
  type InboxMessagePage,
  type InboxUnreadCount
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listInboxMessages(
  page = 1,
  pageSize = 20
): Promise<InboxMessagePage> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-inbox-messages?page=${page}&pageSize=${pageSize}`
  );
  if (!isInboxMessagePage(value)) {
    throw new Error('client.invalid_inbox_message_page');
  }
  return value;
}

export async function getInboxUnreadCount(): Promise<InboxUnreadCount> {
  const value = await request<unknown>(
    '/api/v1/notifications/my-inbox-messages/unread-count'
  );
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }
  return value;
}

export async function markInboxMessageRead(id: string): Promise<InboxMessage> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-inbox-messages/${encodeURIComponent(id)}/read`,
    { method: 'POST' }
  );
  if (!isInboxMessage(value)) {
    throw new Error('client.invalid_inbox_message');
  }
  return value;
}

export async function markAllInboxMessagesRead(): Promise<InboxUnreadCount> {
  const value = await request<unknown>(
    '/api/v1/notifications/my-inbox-messages/read-all',
    { method: 'POST' }
  );
  if (!isInboxUnreadCount(value)) {
    throw new Error('client.invalid_inbox_unread_count');
  }
  return value;
}

export async function sendHostInboxMessage(
  recipientUserId: string,
  title: string,
  content: string
): Promise<InboxMessage> {
  const value = await request<unknown>('/api/v1/notifications/host-inbox-messages', {
    method: 'POST',
    body: JSON.stringify({ recipientUserId, title, content })
  });
  if (!isInboxMessage(value)) {
    throw new Error('client.invalid_inbox_message');
  }
  return value;
}
