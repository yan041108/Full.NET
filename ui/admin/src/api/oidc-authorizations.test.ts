import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  getOidcAuthorization,
  listOidcAuthorizations,
  revokeOidcAuthorization
} from './oidc-authorizations';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));
const requestMock = vi.mocked(request);

const sampleAuthorization = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  applicationId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  clientId: 'admin-spa',
  subject: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
  scopes: ['openid', 'profile', 'offline_access'],
  status: 'valid',
  type: 'permanent',
  creationDateUtc: '2026-07-26T00:00:00Z',
  createdAtUtc: '2026-07-26T00:00:00Z',
  version: 1
};

describe('Vue OIDC authorization API', () => {
  beforeEach(() => requestMock.mockReset());

  it('validates and returns paged list', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleAuthorization],
      page: 1,
      pageSize: 20,
      total: 1
    });
    await expect(listOidcAuthorizations({ clientIdContains: 'admin' }))
      .resolves.toMatchObject({ total: 1 });
  });

  it('loads authorization by id', async () => {
    requestMock.mockResolvedValueOnce(sampleAuthorization);
    await expect(getOidcAuthorization(sampleAuthorization.id))
      .resolves.toMatchObject({ clientId: 'admin-spa' });
  });

  it('revokes authorization via POST', async () => {
    requestMock.mockResolvedValueOnce({ ...sampleAuthorization, status: 'revoked' });
    await expect(revokeOidcAuthorization(sampleAuthorization.id))
      .resolves.toMatchObject({ status: 'revoked' });
  });
});
