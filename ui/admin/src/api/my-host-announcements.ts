import {
  isHostAnnouncementUnreadCount,
  isReceivedHostAnnouncementDetail,
  isReceivedHostAnnouncementPage,
  type HostAnnouncementUnreadCount,
  type ReceivedHostAnnouncementDetail,
  type ReceivedHostAnnouncementListQuery,
  type ReceivedHostAnnouncementPage
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: ReceivedHostAnnouncementListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.title?.trim()) {
    params.set('title', query.title.trim());
  }
  if (query.isRead !== undefined) {
    params.set('isRead', String(query.isRead));
  }
  return params.toString();
}

/** 分页查询当前用户收到的 Host 公告列表，并对响应页做失败关闭校验。 */
export async function listMyHostAnnouncements(
  query: ReceivedHostAnnouncementListQuery = {},
  signal?: AbortSignal
): Promise<ReceivedHostAnnouncementPage> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-host-announcements?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isReceivedHostAnnouncementPage(value)) {
    throw new Error('client.invalid_received_host_announcement_page');
  }

  return value;
}

/** 查询当前用户未读 Host 公告数量。 */
export async function getMyHostAnnouncementUnreadCount(
  signal?: AbortSignal
): Promise<HostAnnouncementUnreadCount> {
  const value = await request<unknown>(
    '/api/v1/notifications/my-host-announcements/unread-count',
    { method: 'GET' },
    signal
  );
  if (!isHostAnnouncementUnreadCount(value)) {
    throw new Error('client.invalid_host_announcement_unread_count');
  }

  return value;
}

/** 查询单条可见 Host 公告详情。 */
export async function getMyHostAnnouncement(
  id: string,
  signal?: AbortSignal
): Promise<ReceivedHostAnnouncementDetail> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-host-announcements/${id}`,
    { method: 'GET' },
    signal
  );
  if (!isReceivedHostAnnouncementDetail(value)) {
    throw new Error('client.invalid_received_host_announcement_detail');
  }

  return value;
}

/** 将单条 Host 公告标记为已读。 */
export async function markMyHostAnnouncementRead(
  id: string,
  signal?: AbortSignal
): Promise<ReceivedHostAnnouncementDetail> {
  const value = await request<unknown>(
    `/api/v1/notifications/my-host-announcements/${id}/read`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' }
    },
    signal
  );
  if (!isReceivedHostAnnouncementDetail(value)) {
    throw new Error('client.invalid_received_host_announcement_detail');
  }

  return value;
}

/** 将当前用户全部可见 Host 公告标记为已读。 */
export async function markAllMyHostAnnouncementsRead(
  signal?: AbortSignal
): Promise<HostAnnouncementUnreadCount> {
  const value = await request<unknown>(
    '/api/v1/notifications/my-host-announcements/read-all',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' }
    },
    signal
  );
  if (!isHostAnnouncementUnreadCount(value)) {
    throw new Error('client.invalid_host_announcement_unread_count');
  }

  return value;
}

/** 导出收件公告列表、详情与未读数模型，供收件页与实时提醒共享同一契约。 */
export type {
  HostAnnouncementUnreadCount,
  ReceivedHostAnnouncementDetail,
  ReceivedHostAnnouncementPage
};
