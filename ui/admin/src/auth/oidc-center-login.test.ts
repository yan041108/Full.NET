import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { adminBrowserSession, beginAdminOidcCenterLogin, clearAdminOidcSessionCredentials, completeAdminOidcCallback, readAdminBrowserCsrfToken } from './oidc-center-login';
import { takeLoginDestination } from './login-destination';
import { configureAuthentication } from '../api/http';
import { browserFixture, csrf } from './browser-session-test-fixture';
beforeEach(() => { clearAdminOidcSessionCredentials(); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); });
describe('BFF callback adapter', () => {
 it('启动固定服务端登录并保留一次邀请接续，不把目标或凭据写入请求 URL', async () => {
  const assign = vi.fn(); vi.stubGlobal('window', { location: { origin: 'http://localhost:3000', assign } });
  await beginAdminOidcCenterLogin('/tenant-context');
  expect(assign).toHaveBeenCalledExactlyOnceWith('/api/v1/identity/browser/login');
  expect(takeLoginDestination()).toBe('/tenant-context');
 });
 it('restores only the server session and CSRF material', async () => {
  const fetch = browserFixture(); await completeAdminOidcCallback(); expect(readAdminBrowserCsrfToken()).toBe(csrf);
  expect(fetch).toHaveBeenCalledOnce(); expect(String(fetch.mock.calls[0]![0])).toContain('/browser/session');
 });
 it('rejects a token-bearing response', async () => {
  browserFixture(() => new Response(JSON.stringify({ csrfToken: csrf, accessToken: 'must-not-leak' })));
  await expect(completeAdminOidcCallback()).rejects.toThrow('oidc_invalid_callback');
 });
 it('does not revive CSRF state from an in-flight restore after logout', async () => {
  let finish!: (value: Response) => void;
  browserFixture(() => new Promise<Response>(resolve => { finish = resolve; }));
  const pending = adminBrowserSession.restore(); await Promise.resolve(); clearAdminOidcSessionCredentials();
  finish(new Response(JSON.stringify({ csrfToken: csrf }))); expect(await pending).toBe(false);
  expect(readAdminBrowserCsrfToken()).toBeUndefined();
 });
 it('removes old SPA credentials without reading them', () => {
  sessionStorage.setItem('fullnet.admin.oidc.refresh', 'old-secret'); sessionStorage.setItem('fullnet.admin.oidc.pkce', 'old-pkce');
  clearAdminOidcSessionCredentials(); expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toBeNull();
  expect(sessionStorage.getItem('fullnet.admin.oidc.pkce')).toBeNull();
 });
});
