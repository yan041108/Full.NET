import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  ADMIN_OIDC_REFRESH_STORAGE_KEY,
  clearOidcRefreshCredential,
  readOidcRefreshCredential,
  writeOidcRefreshCredential
} from './oidc-session-credentials';

describe('oidc session credentials', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('persists and reads refresh credential', () => {
    writeOidcRefreshCredential({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    });
    expect(readOidcRefreshCredential()).toEqual({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    });
    expect(sessionStorage.getItem(ADMIN_OIDC_REFRESH_STORAGE_KEY)).toContain('refresh-token');
  });

  it('clears stored refresh credential', () => {
    writeOidcRefreshCredential({
      refreshToken: 'refresh-token',
      clientId: 'admin-spa'
    });
    clearOidcRefreshCredential();
    expect(readOidcRefreshCredential()).toBeUndefined();
  });
});