import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useSessionStore } from './session';
import { browserFixture, csrf, tenantId, userSessionId } from './browser-session-test-fixture';
vi.mock('../config/identity-auth', () => ({ adminIdentityAuthMode: 'oidc-center', resolveAdminOidcClientId: () => 'fullnet-admin-bff' }));
beforeEach(() => { setActivePinia(createPinia()); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
describe('BFF local session boundaries', () => {
 it('invalidates local state without minting or revoking tokens', async () => {
  const fetch = browserFixture(); const session = useSessionStore(); await session.restore(); const count = fetch.mock.calls.length;
  session.invalidateLocalSession(); expect(session.state).toBe('anonymous'); expect(fetch).toHaveBeenCalledTimes(count);
 });
 it('changes a password with CSRF then requires fresh login', async () => {
  const fetch = browserFixture(); const session = useSessionStore(); await session.restore(); await session.changePassword('old-password', 'new-password');
  const call = fetch.mock.calls.find(([url]) => String(url).endsWith('/browser/password'))!;
  expect(new Headers(call[1]?.headers).get('X-CSRF-Token')).toBe(csrf); expect(session.state).toBe('anonymous');
 });
 it('rejects injecting SPA tokens into the BFF controller', async () => {
  browserFixture(); const session = useSessionStore();
  await expect(session.completeOidcAuthorization({ accessToken: 'injected', tokenType: 'Bearer', expiresAtUtc: '2030-01-01T00:00:00Z' }))
   .rejects.toThrow('identity.browser_interactive_login_required');
 });
});
