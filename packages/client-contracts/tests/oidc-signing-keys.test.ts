import { describe, expect, it } from 'vitest';
import { isOidcSigningKey, isOidcSigningKeyList } from '../src/oidc-signing-keys';

const sampleKey = {
  keyId: 'fixture-oidc-key-a',
  isActive: true,
  hasPrivateKey: true,
  algorithm: 'RS256',
  publicKeyPem: '-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----'
};

describe('OIDC signing key contracts', () => {
  it('recognizes signing key entries', () => {
    expect(isOidcSigningKey(sampleKey)).toBe(true);
    expect(isOidcSigningKey({ ...sampleKey, isActive: 'yes' })).toBe(false);
  });

  it('recognizes list response', () => {
    expect(isOidcSigningKeyList({
      activeSigningKeyId: 'fixture-oidc-key-a',
      usesEphemeralDevelopmentKey: false,
      keys: [sampleKey]
    })).toBe(true);
  });

  it('rejects unknown lifecycle states and nonboolean readiness from the wire', () => {
    expect(isOidcSigningKey({ ...sampleKey, lifecycleState: 'retained', isActivationReady: false, isRetirementReady: true })).toBe(true);
    expect(isOidcSigningKey({ ...sampleKey, lifecycleState: 'forced' })).toBe(false);
    expect(isOidcSigningKey({ ...sampleKey, isActivationReady: 'true' })).toBe(false);
  });
});
