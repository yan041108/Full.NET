import {
  isInboxMessage,
  isInboxMessagePage,
  isInboxUnreadCount,
  type InboxMessage,
  type InboxMessagePage,
  type InboxUnreadCount
} from '@fullnet/client-contracts';
import type { HttpClient } from '../../api/http';

export interface InboxMessagesClient {
  list(page: number, pageSize: number): Promise<InboxMessagePage>;
  getUnreadCount(): Promise<InboxUnreadCount>;
  markRead(messageId: string): Promise<InboxMessage>;
  markAllRead(): Promise<InboxUnreadCount>;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** 创建当前用户站内信客户端；所有响应在进入页面状态前执行运行时守卫。 */
export function createInboxMessagesClient(http: HttpClient): InboxMessagesClient {
  return {
    async list(page, pageSize) {
      const params = new URLSearchParams({
        page: String(page),
        pageSize: String(pageSize)
      });
      const value = await http.request<unknown>({
        path: `/api/v1/notifications/my-inbox-messages?${params.toString()}`
      });
      if (!isInboxMessagePage(value)) {
        throw new TypeError('notifications.inbox.invalid-page');
      }
      return value;
    },
    async getUnreadCount() {
      const value = await http.request<unknown>({
        path: '/api/v1/notifications/my-inbox-messages/unread-count'
      });
      if (!isInboxUnreadCount(value)) {
        throw new TypeError('notifications.inbox.invalid-unread-count');
      }
      return value;
    },
    async markRead(messageId) {
      const normalizedId = requireMessageId(messageId);
      const value = await http.request<unknown>({
        path: `/api/v1/notifications/my-inbox-messages/${normalizedId}/read`,
        method: 'POST'
      });
      if (!isInboxMessage(value)) {
        throw new TypeError('notifications.inbox.invalid-message');
      }
      return value;
    },
    async markAllRead() {
      const value = await http.request<unknown>({
        path: '/api/v1/notifications/my-inbox-messages/read-all',
        method: 'POST'
      });
      if (!isInboxUnreadCount(value)) {
        throw new TypeError('notifications.inbox.invalid-unread-count');
      }
      return value;
    }
  };
}

function requireMessageId(value: string): string {
  if (!guidPattern.test(value)) {
    throw new TypeError('notifications.inbox.invalid-id');
  }
  return value.toLowerCase();
}
