import { afterEach, describe, expect, it, vi } from 'vitest';
import { createIdentitySession } from '../js/core/session.js';
import { configureAuthentication } from '../js/core/http.js';

afterEach(() => {
  vi.unstubAllGlobals();
  configureAuthentication();
  document.cookie = 'fullnet-csrf=; Max-Age=0; Path=/';
});

describe('Layui 管理端会话', () => {
  it('登录后只在内存保存令牌并加载当前用户', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
      .mockResolvedValueOnce(jsonResponse(currentUserResponse()));
    vi.stubGlobal('fetch', fetchMock);
    const localStorageSet = vi.spyOn(Storage.prototype, 'setItem');
    const session = createIdentitySession();

    await session.login('admin', 'FullNet!2026Secure');

    expect(session.snapshot().state).toBe('authenticated');
    expect(session.snapshot().currentUser.username).toBe('admin');
    expect(localStorageSet).not.toHaveBeenCalled();
    const [, meInit] = fetchMock.mock.calls[1];
    expect(new Headers(meInit.headers).get('authorization')).toBe('Bearer access-token');
    session.dispose();
  });

  it('恢复失败时进入匿名状态', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 401,
      code: 'identity.refresh_missing',
      title: '刷新会话不存在'
    }), { status: 401, headers: { 'content-type': 'application/problem+json' } })));
    const session = createIdentitySession();

    await session.restore();

    expect(session.snapshot()).toEqual({ state: 'anonymous', currentUser: undefined });
    session.dispose();
  });

  it('登录后加载当前用户失败时清空已取得的令牌', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('orphan-token')))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 503,
        code: 'identity.profile_unavailable',
        title: '无法加载当前用户'
      }), { status: 503, headers: { 'content-type': 'application/problem+json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const session = createIdentitySession();

    await expect(session.login('admin', 'FullNet!2026Secure')).rejects.toMatchObject({
      code: 'identity.profile_unavailable'
    });
    await session.logout();

    expect(session.snapshot().state).toBe('anonymous');
    const [, logoutInit] = fetchMock.mock.calls[2];
    expect(new Headers(logoutInit.headers).has('authorization')).toBe(false);
    session.dispose();
  });

  it('退出携带 CSRF 并在网络失败时仍清理本地状态', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(tokenResponse('access-token')))
      .mockResolvedValueOnce(jsonResponse(currentUserResponse()))
      .mockRejectedValueOnce(new TypeError('offline'));
    vi.stubGlobal('fetch', fetchMock);
    document.cookie = 'fullnet-csrf=csrf-value; Path=/';
    const session = createIdentitySession();
    await session.login('admin', 'FullNet!2026Secure');

    await session.logout();

    expect(session.snapshot().state).toBe('anonymous');
    const [, logoutInit] = fetchMock.mock.calls[2];
    expect(new Headers(logoutInit.headers).get('x-csrf-token')).toBe('csrf-value');
    session.dispose();
  });
});

function tokenResponse(accessToken) {
  return {
    accessToken,
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUserResponse() {
  return {
    id: 'user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    scope: 'host',
    permissions: [],
    sessionId: 'session-id'
  };
}

function jsonResponse(value) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'content-type': 'application/json' }
  });
}
