import { request } from './http';
import {
  isHostAnnouncement,
  isHostAnnouncementPage,
  type HostAnnouncement,
  type HostAnnouncementPage
} from '@fullnet/client-contracts';

export async function listHostAnnouncements(
  page = 1,
  pageSize = 20
): Promise<HostAnnouncementPage> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostAnnouncementPage(value)) {
    throw new Error('Invalid host announcement page payload.');
  }
  return value;
}

export async function createHostAnnouncement(
  title: string,
  content: string
): Promise<HostAnnouncement> {
  const value = await request<unknown>('/api/v1/notifications/host-announcements', {
    method: 'POST',
    body: { title, content }
  });
  if (!isHostAnnouncement(value)) {
    throw new Error('Invalid host announcement payload.');
  }
  return value;
}

export async function updateHostAnnouncement(
  id: string,
  title: string,
  content: string,
  version: number
): Promise<HostAnnouncement> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: { title, content, version }
    }
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('Invalid host announcement payload.');
  }
  return value;
}

export async function publishHostAnnouncement(
  id: string,
  version: number
): Promise<HostAnnouncement> {
  const value = await request<unknown>(
    `/api/v1/notifications/host-announcements/${encodeURIComponent(id)}/publish`,
    {
      method: 'POST',
      body: { version }
    }
  );
  if (!isHostAnnouncement(value)) {
    throw new Error('Invalid host announcement payload.');
  }
  return value;
}
