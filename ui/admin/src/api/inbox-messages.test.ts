import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getInboxUnreadCount,
  listInboxMessages,
  markInboxMessageRead,
  sendHostInboxMessage
} from './inbox-messages';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const message = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: '系统通知',
  content: '欢迎使用 Full.NET',
  status: 'unread',
  readAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789cd'
};

describe('inbox-messages api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists inbox messages and reads unread count', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [message],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({ unreadCount: 1 });

    await expect(listInboxMessages(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/notifications/my-inbox-messages?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );

    await expect(getInboxUnreadCount()).resolves.toMatchObject({ unreadCount: 1 });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/notifications/my-inbox-messages/unread-count',
      { method: 'GET' },
      undefined
    );
  });

  it('marks read and sends host inbox message', async () => {
    requestMock
      .mockResolvedValueOnce({ ...message, status: 'read', readAtUtc: '2026-07-26T01:00:00Z' })
      .mockResolvedValueOnce(message);

    await expect(markInboxMessageRead(message.id))
      .resolves.toMatchObject({ status: 'read' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/notifications/my-inbox-messages/${message.id}/read`,
      { method: 'POST' },
      undefined
    );

    await expect(sendHostInboxMessage(
      '01912345-6789-7abc-8def-0123456789ef',
      '系统通知',
      '欢迎使用 Full.NET'
    )).resolves.toMatchObject({ title: '系统通知' });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/notifications/host-inbox-messages',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          recipientUserId: '01912345-6789-7abc-8def-0123456789ef',
          title: '系统通知',
          content: '欢迎使用 Full.NET'
        })
      }),
      undefined
    );
  });
});
