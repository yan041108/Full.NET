import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getSuperAdministratorAudits,
  getSuperAdministrators,
  grantSuperAdministrator,
  revokeSuperAdministrator
} from './superAdministrators';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const userId = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60';
const actorUserId = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f61';
const auditId = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f62';

describe('Vue 超级管理员 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验列表与审计响应', async () => {
    requestMock
      .mockResolvedValueOnce([
        {
          userId,
          username: 'admin',
          displayName: '管理员',
          isActive: true
        }
      ])
      .mockResolvedValueOnce([
        {
          id: auditId,
          targetUserId: userId,
          actorUserId,
          eventType: 'grant',
          resultCode: 'grant',
          succeeded: true,
          occurredAtUtc: '2026-07-18T00:00:00Z'
        }
      ]);

    await expect(getSuperAdministrators()).resolves.toHaveLength(1);
    await expect(getSuperAdministratorAudits()).resolves.toHaveLength(1);
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/super-administrators',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/super-administrators/audits?limit=50',
      { method: 'GET' },
      undefined
    );
  });

  it('只通过 JSON 正文发送一次性重认证密码与可选 TOTP', async () => {
    requestMock.mockResolvedValue({ targetUserId: userId, changed: true });

    await grantSuperAdministrator('target', 'secret', '123456');
    await revokeSuperAdministrator(userId, 'secret', '654321');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/super-administrators/grant',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          username: 'target',
          currentPassword: 'secret',
          totpCode: '123456'
        })
      }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/identity/super-administrators/${userId}/revoke`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ currentPassword: 'secret', totpCode: '654321' })
      }),
      undefined
    );
  });
});
