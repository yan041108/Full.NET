import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getCurrentUser } from './me';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const sampleUser = {
  id: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60',
  username: 'admin',
  displayName: 'Admin',
  tenantId: null,
  actorScope: 'host',
  scope: 'host',
  isSuperAdministrator: true,
  permissions: ['identity.users.read'],
  sessionId: '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61',
  preferredLocale: 'zh-CN',
  profileVersion: 1
};

describe('Vue 当前用户 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验当前用户响应', async () => {
    requestMock.mockResolvedValueOnce(sampleUser);

    await expect(getCurrentUser()).resolves.toEqual(sampleUser);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/me',
      { method: 'GET' },
      undefined
    );
  });

  it('拒绝非法 preferredLocale', async () => {
    requestMock.mockResolvedValueOnce({
      ...sampleUser,
      preferredLocale: 'fr-FR'
    });

    await expect(getCurrentUser()).rejects.toThrow('client.invalid_current_user');
  });
});
