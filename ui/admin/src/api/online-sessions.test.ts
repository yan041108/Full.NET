import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { listHostOnlineSessions, revokeHostOnlineSession } from './online-sessions';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleSession = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  userId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  username: 'operator',
  displayName: '运维账号',
  clientId: 'fullnet-admin',
  activeTenantId: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  expiresAtUtc: '2026-08-26T00:00:00Z'
};

describe('Vue Host 在线会话 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleSession],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostOnlineSessions()).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/identity/online-sessions?page=1&pageSize=20'
    );
  });

  it('通过 POST 强制下线会话', async () => {
    requestMock.mockResolvedValueOnce(sampleSession);

    await revokeHostOnlineSession(sampleSession.id);

    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/identity/online-sessions/${sampleSession.id}/revoke`,
      expect.objectContaining({ method: 'POST' })
    );
  });
});
