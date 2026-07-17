import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useSessionStore } from './session';

beforeEach(() => {
  setActivePinia(createPinia());
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 管理端会话', () => {
  it('登录后只在内存保存令牌并加载当前用户', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        accessToken: 'access-token',
        tokenType: 'Bearer',
        expiresAtUtc: '2026-07-17T04:00:00Z'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        id: 'user-id', username: 'admin', displayName: '系统管理员',
        tenantId: null, scope: 'host', permissions: [], sessionId: 'session-id'
      }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);
    const localStorageSet = vi.spyOn(Storage.prototype, 'setItem');

    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    expect(session.state).toBe('authenticated');
    expect(session.currentUser?.username).toBe('admin');
    expect(localStorageSet).not.toHaveBeenCalled();
    const [, meInit] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(new Headers(meInit.headers).get('authorization')).toBe('Bearer access-token');
  });

  it('退出无论服务端结果如何都清理内存状态', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        accessToken: 'access-token',
        tokenType: 'Bearer',
        expiresAtUtc: '2026-07-17T04:00:00Z'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        id: 'user-id', username: 'admin', displayName: '系统管理员',
        tenantId: null, scope: 'host', permissions: [], sessionId: 'session-id'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockRejectedValueOnce(new TypeError('offline'));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();
    await session.login('admin', 'FullNet!2026Secure');

    await session.logout();

    expect(session.state).toBe('anonymous');
    expect(session.currentUser).toBeUndefined();
  });

  it('登录后加载当前用户失败时清空已取得的令牌', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        accessToken: 'orphan-token',
        tokenType: 'Bearer',
        expiresAtUtc: '2026-07-17T04:00:00Z'
      }), { status: 200, headers: { 'content-type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 503,
        code: 'identity.profile_unavailable',
        title: '无法加载当前用户'
      }), { status: 503, headers: { 'content-type': 'application/problem+json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    const session = useSessionStore();

    await expect(session.login('admin', 'FullNet!2026Secure')).rejects.toMatchObject({
      code: 'identity.profile_unavailable'
    });
    await session.logout();

    expect(session.state).toBe('anonymous');
    const [, logoutInit] = fetchMock.mock.calls[2] as [string, RequestInit];
    expect(new Headers(logoutInit.headers).has('authorization')).toBe(false);
  });
});
