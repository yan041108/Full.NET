import {
  isHostAnnouncement,
  isHostAnnouncementPage,
  type CreateHostAnnouncementRequest,
  type HostAnnouncement,
  type HostAnnouncementListQuery,
  type HostAnnouncementPage,
  type UpdateHostAnnouncementRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: HostAnnouncementListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.title?.trim()) {
    params.set('title', query.title.trim());
  }
  if (query.status) {
    params.set('status', query.status);
  }
  if (query.kind) {
    params.set('kind', query.kind);
  }
  if (query.audienceKind) {
    params.set('audienceKind', query.audienceKind);
  }
  return params.toString();
}

/** 分页查询 Host 公告列表，并对响应页做失败关闭校验。 */
export async function listHostAnnouncements(
  query: HostAnnouncementListQuery = {},
  signal?: AbortSignal
): Promise<HostAnnouncementPage> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isHostAnnouncementPage(value)) {
    throw new Error('client.invalid_host_announcement_page');
  }

  return value;
}

/** 创建 Host 公告草稿。 */
export async function createHostAnnouncement(
  body: CreateHostAnnouncementRequest,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await request<unknown>(
    '/api/v1/notifications/host-announcements',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
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
  body: UpdateHostAnnouncementRequest,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements/${id}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
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
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements/${id}/publish`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    },
    signal
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('client.invalid_host_announcement');
  }

  return value;
}

/** 撤回已发布的 Host 公告。 */
export async function retractHostAnnouncement(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostAnnouncement> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements/${id}/retract`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
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
