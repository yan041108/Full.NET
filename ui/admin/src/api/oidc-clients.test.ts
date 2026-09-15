import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  createOidcClient,
  disableOidcClient,
  listOidcClients,
  rotateOidcClientSecret
} from './oidc-clients';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));
const requestMock = vi.mocked(request);

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

describe('Vue OIDC client API', () => {
  beforeEach(() => requestMock.mockReset());

  it('validates and returns paged list', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleClient],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listOidcClients({ clientIdContains: 'admin' }))
      .resolves.toMatchObject({ total: 1 });
  });

  it('creates confidential client and validates one-time secret', async () => {
    requestMock.mockResolvedValueOnce({
      client: sampleClient,
      secret: 'oidc_secret_value'
    });

    await expect(createOidcClient({
      clientId: sampleClient.clientId,
      displayName: sampleClient.displayName,
      redirectUris: sampleClient.redirectUris,
      postLogoutRedirectUris: null,
      scopes: sampleClient.scopes,
      isConfidential: true,
      isFirstParty: true,
      resourceAudience: null
    })).resolves.toMatchObject({ secret: 'oidc_secret_value' });
  });

  it('disables client via POST', async () => {
    requestMock.mockResolvedValueOnce({ ...sampleClient, isDisabled: true });
    await expect(disableOidcClient(sampleClient.id))
      .resolves.toMatchObject({ isDisabled: true });
  });

  it('rotates secret and returns plaintext', async () => {
    requestMock.mockResolvedValueOnce({
      client: sampleClient,
      secret: 'rotated_secret'
    });
    await expect(rotateOidcClientSecret(sampleClient.id))
      .resolves.toMatchObject({ secret: 'rotated_secret' });
  });
});
