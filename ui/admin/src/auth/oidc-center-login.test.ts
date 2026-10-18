import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../api/http', () => ({ apiBaseUrl: 'http://localhost:5149' }));
import {
  ADMIN_OIDC_PKCE_STORAGE_KEY,
  beginAdminOidcCenterLogin,
  clearAdminOidcPkcePending,
  clearAdminOidcSessionCredentials,
  completeAdminOidcCallback,
  readAdminOidcPkcePending,
  refreshAdminOidcAccessToken,
  resolveAdminOidcRedirectUri,
  revokeAdminOidcApplicationSession,
  revokeAdminOidcCenterSession
} from './oidc-center-login';
import { readOidcRefreshCredential, writeOidcRefreshCredential, clearOidcRefreshCredential } from './oidc-session-credentials';

describe('oidc center login helpers', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('resolves redirect uri without fragment for OpenIddict', () => {
    expect(resolveAdminOidcRedirectUri()).not.toContain('#');
    expect(resolveAdminOidcRedirectUri()).toMatch(/\/$/u);
  });

  it('rejects callback when state does not match', async () => {
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({
      verifier: 'verifier',
      state: 'expected',
      nonce: 'nonce'
    }));
    await expect(completeAdminOidcCallback({
      code: 'auth-code',
      state: 'other'
    })).rejects.toThrow('oidc_invalid_state');
    expect(readAdminOidcPkcePending()).toBeUndefined();
  });

  it('clears pending pkce state after successful exchange', async () => {
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({
      verifier: 'verifier',
      state: 'expected',
      nonce: 'nonce'
    }));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      access_token: 'oidc-access-token',
      token_type: 'Bearer',
      expires_in: 120,
      refresh_token: 'oidc-refresh-token'
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    })));
    const token = await completeAdminOidcCallback({
      code: 'auth-code',
      state: 'expected'
    });
    expect(token.accessToken).toBe('oidc-access-token');
    expect(readOidcRefreshCredential()).toEqual({
      refreshToken: 'oidc-refresh-token',
      clientId: 'admin-spa'
    });
    expect(readAdminOidcPkcePending()).toBeUndefined();
    clearAdminOidcPkcePending();
  });

  it('refreshes access token from persisted refresh credential', async () => {
    sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
      refreshToken: 'stored-refresh-token',
      clientId: 'admin-spa'
    }));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      access_token: 'refreshed-access-token',
      token_type: 'Bearer',
      expires_in: 120,
      refresh_token: 'rotated-refresh-token'
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    })));
    const token = await refreshAdminOidcAccessToken();
    expect(token?.accessToken).toBe('refreshed-access-token');
    expect(readOidcRefreshCredential()).toEqual({
      refreshToken: 'rotated-refresh-token',
      clientId: 'admin-spa'
    });
    clearAdminOidcSessionCredentials();
  });

  it('revokes application session for admin client', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const revoked = await revokeAdminOidcApplicationSession();
    expect(revoked).toBe(true);
  });

  it('revokes center session for admin', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const revoked = await revokeAdminOidcCenterSession();
    expect(revoked).toBe(true);
  });

  it.each(['clear', 'replace', 'same-after-clear'])('迟到刷新成功不能覆盖已变化凭据：%s', async action => {
    const original = { refreshToken: 'old-refresh', clientId: 'admin-spa' };
    writeOidcRefreshCredential(original);
    let finish!: (value: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })));
    const pending = refreshAdminOidcAccessToken();
    clearOidcRefreshCredential();
    if (action !== 'clear') writeOidcRefreshCredential(action === 'replace' ? { ...original, refreshToken: 'new-refresh' } : original);
    const expected = readOidcRefreshCredential();
    finish(new Response(JSON.stringify({ access_token: 'old-access', token_type: 'Bearer', expires_in: 120, refresh_token: 'old-rotated' }), { status: 200 }));
    expect(await pending).toBeUndefined(); expect(readOidcRefreshCredential()).toEqual(expected);
  });

  it('迟到刷新失败不能清除新凭据', async () => {
    writeOidcRefreshCredential({ refreshToken: 'old-refresh', clientId: 'admin-spa' });
    let fail!: (reason: Error) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((_, reject) => { fail = reject; })));
    const pending = refreshAdminOidcAccessToken();
    writeOidcRefreshCredential({ refreshToken: 'new-refresh', clientId: 'admin-spa' });
    fail(new Error('network'));
    expect(await pending).toBeUndefined(); expect(readOidcRefreshCredential()?.refreshToken).toBe('new-refresh');
  });

  it('旧回调不得覆盖新凭据或清除新 PKCE 请求', async () => {
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({ verifier: 'old-verifier', state: 'expected', nonce: 'old-nonce' }));
    let finish!: (value: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })));
    const pending = completeAdminOidcCallback({ code: 'auth-code', state: 'expected' });
    clearAdminOidcSessionCredentials();
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({ verifier: 'new-verifier', state: 'new-state', nonce: 'new-nonce' }));
    writeOidcRefreshCredential({ refreshToken: 'new-refresh', clientId: 'admin-spa' });
    finish(new Response(JSON.stringify({ access_token: 'old-access', token_type: 'Bearer', expires_in: 120, refresh_token: 'old-refresh' }), { status: 200 }));
    await expect(pending).rejects.toThrow('oidc_callback_cancelled');
    expect(readOidcRefreshCredential()?.refreshToken).toBe('new-refresh'); expect(readAdminOidcPkcePending()?.state).toBe('new-state');
  });

  it('取消回调后即使传输仍返回成功也不保存凭据', async () => {
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({ verifier: 'verifier', state: 'expected', nonce: 'nonce' }));
    let finish!: (value: Response) => void;
    const fetchMock = vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })); vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();
    const pending = (completeAdminOidcCallback as (query: Record<string, string>, signal: AbortSignal) => Promise<unknown>)({ code: 'code', state: 'expected' }, controller.signal);
    controller.abort(); finish(new Response(JSON.stringify({ access_token: 'old-access', token_type: 'Bearer', expires_in: 120, refresh_token: 'old-refresh' }), { status: 200 }));
    await expect(pending).rejects.toThrow('oidc_callback_cancelled');
    expect(readOidcRefreshCredential()).toBeUndefined(); expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].signal).toBe(controller.signal);
  });

  it('退出后的 PKCE 生成结果不能重新跳转或保存请求', async () => {
    let finish!: (value: ArrayBuffer) => void;
    vi.spyOn(crypto.subtle, 'digest').mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    const assign = vi.fn(); vi.stubGlobal('window', { location: { origin: 'http://localhost', pathname: '/', search: '', assign } });
    const pending = beginAdminOidcCenterLogin(); clearAdminOidcSessionCredentials(); finish(new ArrayBuffer(32));
    await expect(pending).rejects.toThrow('oidc_login_cancelled'); expect(assign).not.toHaveBeenCalled(); expect(readAdminOidcPkcePending()).toBeUndefined();
  });

  it('新交换不含 refresh 时不能沿用旧账号凭据', async () => {
    writeOidcRefreshCredential({ refreshToken: 'old-user-refresh', clientId: 'admin-spa' });
    sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify({ verifier: 'verifier', state: 'expected', nonce: 'nonce' }));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ access_token: 'new-access', token_type: 'Bearer', expires_in: 120 }), { status: 200 })));
    const handoff = vi.fn();
    await (completeAdminOidcCallback as (query: Record<string, string>, signal: undefined, handoff: (revision: number) => void) => Promise<unknown>)({ code: 'code', state: 'expected' }, undefined, handoff);
    expect(readOidcRefreshCredential()).toBeUndefined(); expect(handoff).toHaveBeenCalledOnce();
  });
});
