import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { activateOidcSigningKey, listOidcSigningKeys } from './oidc-signing-keys';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));
const requestMock = vi.mocked(request);

const sampleList = {
  activeSigningKeyId: 'fixture-oidc-key-a',
  usesEphemeralDevelopmentKey: false,
  keys: [{
    keyId: 'fixture-oidc-key-a',
    isActive: true,
    hasPrivateKey: true,
    algorithm: 'RS256',
    publicKeyPem: '-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----'
  }]
};

describe('Vue OIDC signing key API', () => {
  beforeEach(() => requestMock.mockReset());

  it('validates and returns signing key list', async () => {
    requestMock.mockResolvedValueOnce(sampleList);
    await expect(listOidcSigningKeys()).resolves.toMatchObject({
      activeSigningKeyId: 'fixture-oidc-key-a'
    });
  });

  it('activates signing key via POST', async () => {
    requestMock.mockResolvedValueOnce(sampleList);
    await expect(activateOidcSigningKey('fixture-oidc-key-a'))
      .resolves.toMatchObject({ activeSigningKeyId: 'fixture-oidc-key-a' });
  });
});