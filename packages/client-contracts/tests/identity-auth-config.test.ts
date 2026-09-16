import { describe, expect, it } from 'vitest';
import { resolveAdminIdentityAuthMode } from '../src/identity-auth-config';

describe('resolveAdminIdentityAuthMode', () => {
  it('默认回落到 legacy', () => {
    expect(resolveAdminIdentityAuthMode(undefined)).toBe('legacy');
    expect(resolveAdminIdentityAuthMode(null)).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('')).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('legacy')).toBe('legacy');
    expect(resolveAdminIdentityAuthMode('unknown')).toBe('legacy');
  });

  it('仅显式 oidc-center 启用中心 OIDC 登录', () => {
    expect(resolveAdminIdentityAuthMode('oidc-center')).toBe('oidc-center');
  });
});