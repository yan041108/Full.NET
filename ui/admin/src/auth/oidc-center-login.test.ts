import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { adminBrowserSession, clearAdminOidcSessionCredentials, completeAdminOidcCallback, readAdminBrowserCsrfToken } from './oidc-center-login';
import { configureAuthentication } from '../api/http';
import { browserFixture, csrf } from './browser-session-test-fixture';
beforeEach(() => { clearAdminOidcSessionCredentials(); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); });
describe('BFF callback adapter', () => {
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
