import { describe, expect, it } from 'vitest';
import {
  isCurrentUserResponse,
  isTokenResponse
} from '../src/identity';

describe('身份会话契约', () => {
  it('识别合法令牌响应', () => {
    expect(isTokenResponse({
      accessToken: 'token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z'
    })).toBe(true);
    expect(isTokenResponse({ accessToken: 'token' })).toBe(false);
  });

  it('识别当前用户安全摘要', () => {
    expect(isCurrentUserResponse({
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      scope: 'host',
      permissions: [],
      sessionId: 'session-id'
    })).toBe(true);
    expect(isCurrentUserResponse({ id: 'user-id', permissions: 'all' })).toBe(false);
  });
});
