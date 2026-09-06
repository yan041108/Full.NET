import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  getMyHostAnnouncementUnreadCount,
  listMyHostAnnouncements,
  markAllMyHostAnnouncementsRead,
  markMyHostAnnouncementRead
} from './my-host-announcements';

vi.mock('./http', () => ({
  request: vi.fn()
}));

const requestMock = vi.mocked(request);

const listItem = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: '维护通知',
  kind: 'announcement',
  audienceKind: 'all',
  publishedAtUtc: '2026-07-26T01:00:00Z',
  isRead: false,
  readAtUtc: null
};

const detail = {
  ...listItem,
  content: '系统将于今晚维护',
  publishedByUserId: '01912345-6789-7abc-8def-0123456789ac'
};

describe('my-host-announcements api', () => {
  beforeEach(() => {
    requestMock.mockReset();
  });

  it('lists received announcements with read filter', async () => {
    requestMock.mockResolvedValueOnce({
      items: [listItem],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listMyHostAnnouncements({
      page: 1,
      pageSize: 20,
      title: '维护',
      isRead: false
    })).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/notifications/my-host-announcements?page=1&pageSize=20&title=%E7%BB%B4%E6%8A%A4&isRead=false',
      { method: 'GET' },
      undefined
    );
  });

  it('loads unread count and marks announcements read', async () => {
    requestMock
      .mockResolvedValueOnce({ unreadCount: 2 })
      .mockResolvedValueOnce({
        ...detail,
        isRead: true,
        readAtUtc: '2026-07-26T02:00:00Z'
      })
      .mockResolvedValueOnce({ unreadCount: 0 });

    await expect(getMyHostAnnouncementUnreadCount()).resolves.toMatchObject({ unreadCount: 2 });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/my-host-announcements/unread-count',
      { method: 'GET' },
      undefined
    );

    await expect(markMyHostAnnouncementRead(listItem.id)).resolves.toMatchObject({ isRead: true });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/notifications/my-host-announcements/${listItem.id}/read`,
      expect.objectContaining({ method: 'POST' }),
      undefined
    );

    await expect(markAllMyHostAnnouncementsRead()).resolves.toMatchObject({ unreadCount: 0 });
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/notifications/my-host-announcements/read-all',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });
});
