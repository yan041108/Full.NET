import { describe, expect, it } from 'vitest';
import {
  buildOidcAuthorizeUrl,
  createOidcAuthorizationRequest,
  createOidcPkcePair,
  validateOidcCallbackState
} from '../src/oidc-interactive-auth';

describe('oidc interactive auth helpers', () => {
  it('生成 PKCE S256 对', async () => {
    const pair = await createOidcPkcePair();
    expect(pair.verifier.length).toBeGreaterThan(20);
    expect(pair.challenge.length).toBeGreaterThan(20);
    expect(pair.verifier).not.toBe(pair.challenge);
  });

  it('生成授权请求包含 state 与 nonce', async () => {
    const request = await createOidcAuthorizationRequest();
    expect(request.verifier).toBeTruthy();
    expect(request.challenge).toBeTruthy();
    expect(request.state).toBeTruthy();
    expect(request.nonce).toBeTruthy();
  });

  it('构造授权 URL 并保留关键参数', async () => {
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

  it('校验回调 state', () => {
    expect(validateOidcCallbackState('expected', 'expected')).toBe(true);
    expect(validateOidcCallbackState('expected', 'other')).toBe(false);
    expect(validateOidcCallbackState('expected', '')).toBe(false);
    expect(validateOidcCallbackState('expected', null)).toBe(false);
  });
});