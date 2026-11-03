import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useSessionStore } from './session';
import { browserFixture, csrf, tenantId, userSessionId } from './browser-session-test-fixture';
vi.mock('../config/identity-auth', () => ({ adminIdentityAuthMode: 'oidc-center', resolveAdminOidcClientId: () => 'fullnet-admin-bff' }));
beforeEach(() => { setActivePinia(createPinia()); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
describe('BFF realtime revocation', () => {
 it('clears the matching session without calling logout or refresh endpoints', async () => {
  const fetch = browserFixture(); const session = useSessionStore(); await session.restore(); const count = fetch.mock.calls.length;
  expect(session.handleRemoteSessionRevoke(userSessionId)).toBe(true); expect(session.state).toBe('anonymous');
  expect(session.readCsrfToken()).toBeUndefined(); expect(fetch).toHaveBeenCalledTimes(count);
 });
 it('ignores another device session', async () => {
  browserFixture(); const session = useSessionStore(); await session.restore();
  expect(session.handleRemoteSessionRevoke(tenantId)).toBe(false); expect(session.state).toBe('authenticated');
 });
});
