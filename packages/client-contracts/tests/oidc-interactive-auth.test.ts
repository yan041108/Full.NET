import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  buildOidcAuthorizeUrl,
  createOidcAuthorizationRequest,
  createOidcPkcePair,
  exchangeOidcAuthorizationCode,
  mapOidcTokenEndpointToTokenResponse,
  validateOidcCallbackState
} from '../src/oidc-interactive-auth';

describe('oidc interactive auth helpers', () => {
  it('generates PKCE S256 pair', async () => {
    const pair = await createOidcPkcePair();
    expect(pair.verifier.length).toBeGreaterThan(20);
    expect(pair.challenge.length).toBeGreaterThan(20);
    expect(pair.verifier).not.toBe(pair.challenge);
  });

  it('generates authorization request with state and nonce', async () => {
    const request = await createOidcAuthorizationRequest();
    expect(request.verifier).toBeTruthy();
    expect(request.challenge).toBeTruthy();
    expect(request.state).toBeTruthy();
    expect(request.nonce).toBeTruthy();
  });

  it('builds authorize url with required params', async () => {
    const request = await createOidcAuthorizationRequest();
    const url = buildOidcAuthorizeUrl({
      apiBase: 'http://localhost:5149/',
      clientId: 'admin-spa',
      redirectUri: 'http://localhost:5173/#/identity/oidc/callback',
      challenge: request.challenge,
      state: request.state,
      nonce: request.nonce,
      scope: 'openid profile offline_access',
      extraParams: { prompt: 'login' }
    });
    const parsed = new URL(url);
    expect(parsed.origin).toBe('http://localhost:5149');
    expect(parsed.pathname).toBe('/connect/authorize');
    expect(parsed.searchParams.get('client_id')).toBe('admin-spa');
    expect(parsed.searchParams.get('code_challenge_method')).toBe('S256');
    expect(parsed.searchParams.get('state')).toBe(request.state);
    expect(parsed.searchParams.get('nonce')).toBe(request.nonce);
    expect(parsed.searchParams.get('prompt')).toBe('login');
  });

  it('validates callback state', () => {
    expect(validateOidcCallbackState('expected', 'expected')).toBe(true);
    expect(validateOidcCallbackState('expected', 'other')).toBe(false);
    expect(validateOidcCallbackState('expected', '')).toBe(false);
    expect(validateOidcCallbackState('expected', null)).toBe(false);
  });
});

describe('OIDC token endpoint mapping', () => {
  it('maps access_token and expires_in to TokenResponse', () => {
    const mapped = mapOidcTokenEndpointToTokenResponse({
      access_token: 'oidc-access-token',
      token_type: 'Bearer',
      expires_in: 3600
    });
    expect(mapped.accessToken).toBe('oidc-access-token');
    expect(mapped.tokenType).toBe('Bearer');
    expect(mapped.expiresAtUtc).toBeTruthy();
  });

  it('exchanges authorization code through token endpoint', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      access_token: 'oidc-access-token',
      token_type: 'Bearer',
      expires_in: 120
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    })));
    const token = await exchangeOidcAuthorizationCode({
      apiBase: 'http://localhost:5149',
      clientId: 'admin-spa',
      redirectUri: 'http://localhost:5173/#/identity/oidc/callback',
      code: 'auth-code',
      verifier: 'verifier-value'
    });
    expect(token.accessToken).toBe('oidc-access-token');
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});