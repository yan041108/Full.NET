import { describe, expect, it } from 'vitest';
import {
  isCurrentUserResponse,
  isLocalePreferenceResponse,
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
    expect(isTokenResponse({
      accessToken: 'token',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-07-17T04:00:00Z',
      preferredLocale: 'en-US',
      profileVersion: 2
    })).toBe(false);
  });

  it('识别当前用户安全摘要', () => {
    expect(isCurrentUserResponse({
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: true,
      permissions: [],
      sessionId: 'session-id',
      preferredLocale: 'en-US',
      profileVersion: 2
    })).toBe(true);
    expect(isCurrentUserResponse({
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      scope: 'host',
      isSuperAdministrator: true,
      permissions: [],
      sessionId: 'session-id',
      preferredLocale: 'en-US',
      profileVersion: 2
    })).toBe(false);
    expect(isCurrentUserResponse({
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: true,
      permissions: [],
      sessionId: 'session-id',
      preferredLocale: 'fr-FR',
      profileVersion: 2
    })).toBe(false);
    expect(isCurrentUserResponse({
      id: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      permissions: [],
      sessionId: 'session-id',
      preferredLocale: 'en-US',
      profileVersion: 2
    })).toBe(false);
    expect(isCurrentUserResponse({ id: 'user-id', permissions: 'all' })).toBe(false);
  });

  it('识别账号语言偏好响应并拒绝未知语言或非正整数版本', () => {
    expect(isLocalePreferenceResponse({
      preferredLocale: 'zh-CN',
      profileVersion: 1
    })).toBe(true);
    expect(isLocalePreferenceResponse({
      preferredLocale: 'en-GB',
      profileVersion: 1
    })).toBe(false);
    expect(isLocalePreferenceResponse({
      preferredLocale: 'en-US',
      profileVersion: 0
    })).toBe(false);
  });
});
