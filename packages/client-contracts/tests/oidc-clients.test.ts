import { describe, expect, it } from 'vitest';
import {
  isCreateOidcClientResult,
  isOidcClient,
  isOidcClientPage,
  isRotateOidcClientSecretResult
} from '../src/oidc-clients';

const sampleClient = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  clientId: 'admin-spa',
  displayName: 'Admin SPA',
  clientType: 'confidential',
  redirectUris: ['https://localhost:5173/oauth/callback'],
  postLogoutRedirectUris: [],
  scopes: ['openid', 'profile'],
  isFirstParty: true,
  resourceAudience: null,
  isDisabled: false,
  createdAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

describe('OIDC client contracts', () => {
  it('识别뿯宽户뿯窽뿯宽体', () => {
    expect(isOidcClient(sampleClient)).toBe(true);
    expect(isOidcClient({ ...sampleClient, id: 'bad' })).toBe(false);
  });

  it('识别创뿯庽与붿换뿯纽뿯枽', () => {
    expect(isCreateOidcClientResult({ client: sampleClient, secret: 'secret' })).toBe(true);
    expect(isCreateOidcClientResult({ client: sampleClient, secret: null })).toBe(true);
    expect(isRotateOidcClientSecretResult({ client: sampleClient, secret: 'secret' })).toBe(true);
  });

  it('识别分붿뿯纽뿯枽', () => {
    expect(isOidcClientPage({
      items: [sampleClient],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });
});
