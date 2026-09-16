import { afterEach, describe, expect, it, vi } from 'vitest';

describe('resolveAdminOidcClientId', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('defaults to admin-spa when client id is unset', async () => {
    vi.unstubAllEnvs();
    const { resolveAdminOidcClientId } = await import('./identity-auth');
    expect(resolveAdminOidcClientId()).toBe('admin-spa');
  });

  it('honors configured VITE_IDENTITY_OIDC_CLIENT_ID', async () => {
    vi.stubEnv('VITE_IDENTITY_OIDC_CLIENT_ID', 'e2e-admin-oidc-spa');
    const { resolveAdminOidcClientId } = await import('./identity-auth');
    expect(resolveAdminOidcClientId()).toBe('e2e-admin-oidc-spa');
  });
});

describe('adminIdentityAuthMode', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('defaults to legacy when auth mode is unset', async () => {
    vi.unstubAllEnvs();
    const { adminIdentityAuthMode } = await import('./identity-auth');
    expect(adminIdentityAuthMode).toBe('legacy');
  });

  it('enables oidc-center only when explicitly configured', async () => {
    vi.stubEnv('VITE_IDENTITY_AUTH_MODE', 'oidc-center');
    const { adminIdentityAuthMode } = await import('./identity-auth');
    expect(adminIdentityAuthMode).toBe('oidc-center');
  });

  it('falls back to legacy for unknown auth mode values', async () => {
    vi.stubEnv('VITE_IDENTITY_AUTH_MODE', 'experimental');
    const { adminIdentityAuthMode } = await import('./identity-auth');
    expect(adminIdentityAuthMode).toBe('legacy');
  });
});