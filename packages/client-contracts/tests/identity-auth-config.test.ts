import { describe, expect, it } from 'vitest';
import { resolveAdminIdentityAuthMode } from '../src/identity-auth-config';

describe('resolveAdminIdentityAuthMode', () => {
  it('defaults to legacy', () => {
    expect(resolveAdminIdentityAuthMode(undefined)).toBe('legacy');
    expect(resolveAdminIdentityAuthMode(null)).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('')).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('legacy')).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('unknown')).toBe('legacy');
  });

  it('enables oidc-center only when explicitly configured', () => {
    expect(resolveAdminIdentityAuthMode('oidc-center')).toBe('oidc-center');
  });
});