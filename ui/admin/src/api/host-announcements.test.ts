import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement,
  retractHostAnnouncement
} from './host-announcements';

vi.mock('./http', () => ({
  request: vi.fn()
}));

const requestMock = vi.mocked(request);

const announcement = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: '维护通知',
  content: '系统将于今晚维护',
  kind: 'announcement',
  audienceKind: 'all',
  status: 'draft',
  publishedAtUtc: null,
  publishedByUserId: null,
  retractedAtUtc: null,
  retractedByUserId: null,
  targetUserIds: [],
  targetOrganizations: [],
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('host-announcements api', () => {
  beforeEach(() => {
    requestMock.mockReset();
  });

  it('lists announcements with server-side filters', async () => {
    requestMock.mockResolvedValueOnce({
      items: [announcement],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostAnnouncements({
      page: 1,
      pageSize: 20,
      status: 'published'
    })).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/notifications/host-announcements?page=1&pageSize=20&status=published',
      { method: 'GET' },
      undefined
    );
  });

  it('creates, publishes and retracts an announcement', async () => {
    requestMock
      .mockResolvedValueOnce({
        ...announcement,
        kind: 'notice'
      })
      .mockResolvedValueOnce({
        ...announcement,
        status: 'published',
        publishedAtUtc: '2026-07-26T01:00:00Z',
        publishedByUserId: '01912345-6789-7abc-8def-0123456789ac',
        version: 2
      })
      .mockResolvedValueOnce({
        ...announcement,
        status: 'retracted',
        publishedAtUtc: '2026-07-26T01:00:00Z',
        publishedByUserId: '01912345-6789-7abc-8def-0123456789ac',
        retractedAtUtc: '2026-07-26T02:00:00Z',
        retractedByUserId: '01912345-6789-7abc-8def-0123456789ac',
        version: 3
      });

    await expect(createHostAnnouncement({
      title: '维护通知',
      content: '系统将于今晚维护',
      kind: 'notice',
      audienceKind: 'all'
    })).resolves.toMatchObject({ title: '维护通知', kind: 'notice' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/host-announcements',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          title: '维护通知',
          content: '系统将于今晚维护',
          kind: 'notice',
          audienceKind: 'all'
        })
      }),
      undefined
    );

    await expect(publishHostAnnouncement(announcement.id, 1))
      .resolves.toMatchObject({ status: 'published' });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/notifications/host-announcements/${announcement.id}/publish`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 1 })
      }),
      undefined
    );

    await expect(retractHostAnnouncement(announcement.id, 2))
      .resolves.toMatchObject({ status: 'retracted' });
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/notifications/host-announcements/${announcement.id}/retract`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 2 })
      }),
      undefined
    );
  });
});
