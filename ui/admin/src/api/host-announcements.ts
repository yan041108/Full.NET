import {
  isHostAnnouncement,
  isHostAnnouncementPage,
  notificationsCreateHostAnnouncement,
  notificationsListHostAnnouncements,
  notificationsPublishHostAnnouncement,
  notificationsUpdateHostAnnouncement,
  type HostAnnouncement,
  type HostAnnouncementPage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 公告列表，并对响应页做失败关闭校验。 */
export async function listHostAnnouncements(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostAnnouncementPage> {
  const value = await notificationsListHostAnnouncements(
    http,
    { page, pageSize },
    signal
  );
  if (!isHostAnnouncementPage(value)) {
    throw new Error('client.invalid_host_announcement_page');
  }

  return value;
}

/** 创建 Host 公告草稿。 */
export async function createHostAnnouncement(
  title: string,
  content: string,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await notificationsCreateHostAnnouncement(
    http,
    { body: { title, content } },
    signal
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('client.invalid_host_announcement');
  }

  return value;
}

/** 更新 Host 公告草稿，并携带版本号维持乐观并发。 */
export async function updateHostAnnouncement(
  id: string,
  title: string,
  content: string,
  version: number,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await notificationsUpdateHostAnnouncement(
    http,
    {
      announcementId: id,
      body: { title, content, version }
    },
    signal
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('client.invalid_host_announcement');
  }

  return value;
}

/** 发布 Host 公告。 */
export async function publishHostAnnouncement(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await notificationsPublishHostAnnouncement(
    http,
    {
      announcementId: id,
      body: { version }
    },
    signal
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('client.invalid_host_announcement');
  }

  return value;
}

/** 导出公告列表与单条公告模型，供公告管理页列表、编辑器与发布流程复用同一契约。 */
export type { HostAnnouncement, HostAnnouncementPage };
