import { describe, expect, it } from 'vitest';
import { isOidcAuthorization, isOidcAuthorizationPage } from '../src/oidc-authorizations';

const sampleAuthorization = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  applicationId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  clientId: 'admin-spa',
  subject: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
  scopes: ['openid', 'profile'],
  status: 'valid',
  type: 'permanent',
  creationDateUtc: '2026-07-26T00:00:00Z',
  createdAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

describe('OIDC authorization contracts', () => {
  it('识别뿯掽权뿯宽体', () => {
    expect(isOidcAuthorization(sampleAuthorization)).toBe(true);
    expect(isOidcAuthorization({ ...sampleAuthorization, status: 1 })).toBe(false);
  });

  it('识别分붿뿯纽뿯枽', () => {
    expect(isOidcAuthorizationPage({
      items: [sampleAuthorization],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });
});
