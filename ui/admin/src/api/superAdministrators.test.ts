import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  getSuperAdministratorAudits,
  getSuperAdministrators,
  grantSuperAdministrator,
  revokeSuperAdministrator
} from './superAdministrators';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

describe('Vue 超级管理员 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验列表与审计响应', async () => {
    requestMock
      .mockResolvedValueOnce([{ userId: 'u1', username: 'admin', displayName: '管理员', isActive: true }])
      .mockResolvedValueOnce([{ id: 'a1', targetUserId: 'u1', actorUserId: 'u2', eventType: 'grant', resultCode: 'grant', succeeded: true, occurredAtUtc: '2026-07-18T00:00:00Z' }]);

    await expect(getSuperAdministrators()).resolves.toHaveLength(1);
    await expect(getSuperAdministratorAudits()).resolves.toHaveLength(1);
  });

  it('只通过 JSON 正文发送一次性重认证密码与可选 TOTP', async () => {
    requestMock.mockResolvedValue({ targetUserId: 'target', changed: true });

    await grantSuperAdministrator('target', 'secret', '123456');
    await revokeSuperAdministrator('target', 'secret', '654321');

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
      })
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/super-administrators/target/revoke',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ currentPassword: 'secret', totpCode: '654321' })
      })
    );
  });
});
