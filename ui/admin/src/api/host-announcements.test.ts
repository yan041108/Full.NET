import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement
} from './host-announcements';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const announcement = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: '维护通知',
  content: '系统将于今晚维护',
  status: 'draft',
  publishedAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('host-announcements api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists announcements', async () => {
    requestMock.mockResolvedValueOnce({
      items: [announcement],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostAnnouncements(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/notifications/host-announcements?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('creates and publishes an announcement', async () => {
    requestMock
      .mockResolvedValueOnce(announcement)
      .mockResolvedValueOnce({
        ...announcement,
        status: 'published',
        publishedAtUtc: '2026-07-26T01:00:00Z',
        version: 2
      });

    await expect(createHostAnnouncement('维护通知', '系统将于今晚维护'))
      .resolves.toMatchObject({ title: '维护通知' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/host-announcements',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ title: '维护通知', content: '系统将于今晚维护' })
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
  });
});
