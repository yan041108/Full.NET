import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../api/http', () => ({ apiBaseUrl: 'http://localhost:5149' }));
import {
  ADMIN_OIDC_PKCE_STORAGE_KEY,
  clearAdminOidcPkcePending,
  clearAdminOidcSessionCredentials,
  completeAdminOidcCallback,
  readAdminOidcPkcePending,
  refreshAdminOidcAccessToken,
  resolveAdminOidcRedirectUri,
  revokeAdminOidcApplicationSession
} from './oidc-center-login';
import { readOidcRefreshCredential } from './oidc-session-credentials';

describe('oidc center login helpers', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('resolves hash-router callback redirect uri', () => {
    expect(resolveAdminOidcRedirectUri()).toContain('#/identity/oidc/callback');
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
});