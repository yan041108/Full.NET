import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useSessionStore } from './session';
import { browserFixture, csrf, tenantId, userSessionId } from './browser-session-test-fixture';
vi.mock('../config/identity-auth', () => ({ adminIdentityAuthMode: 'oidc-center', resolveAdminOidcClientId: () => 'fullnet-admin-bff' }));
beforeEach(() => { setActivePinia(createPinia()); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
describe('BFF session restoration', () => {
 it('restores with Cookie and exposes no OAuth credentials', async () => {
  const fetch = browserFixture(); const session = useSessionStore(); await session.restore();
  expect(session.state).toBe('authenticated'); expect(session.readAccessToken()).toBeUndefined();
  for (const [, init] of fetch.mock.calls) { expect(new Headers(init?.headers).has('authorization')).toBe(false); expect(init?.credentials).toBe('include'); }
  expect(sessionStorage.getItem('fullnet.admin.oidc.refresh')).toBeNull();
 });
 it('serializes concurrent restoration without token exchanges in the browser', async () => {
  let active = 0, maximum = 0;
  const fetch = browserFixture(async url => { if (!url.endsWith('/browser/session')) return undefined;
   maximum = Math.max(maximum, ++active); await new Promise(resolve => setTimeout(resolve, 10)); active--;
   return new Response(JSON.stringify({ csrfToken: csrf, expiresAtUtc: '2030-01-01T00:00:00Z' }));
  });
  const session = useSessionStore(); await Promise.all([session.restore(), session.restore()]);
  expect(session.state).toBe('authenticated'); expect(maximum).toBe(1);
  expect(fetch.mock.calls.some(([url]) => String(url).includes('/connect/token'))).toBe(false);
 });
 it('clears local state when the authority rejects a browser ticket', async () => {
  browserFixture(url => url.endsWith('/browser/session') ? new Response('{}', { status: 401 }) : undefined);
  const session = useSessionStore(); await session.restore(); expect(session.state).toBe('anonymous');
 });
});
