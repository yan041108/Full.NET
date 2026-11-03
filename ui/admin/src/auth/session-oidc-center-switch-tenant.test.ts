import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { configureAuthentication } from '../api/http';
import { useSessionStore } from './session';
import { browserFixture, csrf, tenantId, userSessionId } from './browser-session-test-fixture';
vi.mock('../config/identity-auth', () => ({ adminIdentityAuthMode: 'oidc-center', resolveAdminOidcClientId: () => 'fullnet-admin-bff' }));
beforeEach(() => { setActivePinia(createPinia()); sessionStorage.clear(); });
afterEach(() => { configureAuthentication(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
describe('BFF tenant mutation', () => {
 it('accepts a no-content response and reloads the trusted tenant snapshot', async () => {
  const fetch = browserFixture(); const session = useSessionStore(); await session.restore(); await session.switchTenant(tenantId);
  expect(session.currentUser?.tenantId).toBe(tenantId); expect(session.readAccessToken()).toBeUndefined();
  const call = fetch.mock.calls.find(([url]) => String(url).endsWith('/tenancy/context'))!;
  expect(new Headers(call[1]?.headers).get('X-CSRF-Token')).toBe(csrf);
 });
 it('restores authority and retries a single context conflict', async () => {
  let mutations = 0; browserFixture(url => url.endsWith('/tenancy/context') && mutations++ === 0
   ? new Response(JSON.stringify({ type: 'about:blank', title: 'Conflict', status: 409, code: 'identity.session_context_conflict' }), { status: 409, headers: { 'content-type': 'application/problem+json' } }) : undefined);
  const session = useSessionStore(); await session.restore(); await session.switchTenant(tenantId);
  expect(mutations).toBe(2); expect(session.currentUser?.tenantId).toBe(tenantId);
 });
 it('fails closed if a mutation cannot be confirmed', async () => {
  browserFixture(url => url.endsWith('/tenancy/context') ? new Response('{}', { status: 503 }) : undefined);
  const session = useSessionStore(); await session.restore(); await session.switchTenant(tenantId).catch(() => undefined);
  expect(session.state).toBe('anonymous');
 });
});
