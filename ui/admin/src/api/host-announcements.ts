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

export type { HostAnnouncement, HostAnnouncementPage };
