export interface OidcSigningKey {
  keyId: string;
  isActive: boolean;
  hasPrivateKey: boolean;
  algorithm: string;
  publicKeyPem: string;
}

export interface OidcSigningKeyList {
  activeSigningKeyId: string;
  usesEphemeralDevelopmentKey: boolean;
  keys: OidcSigningKey[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isOidcSigningKey(value: unknown): value is OidcSigningKey {
  return isRecord(value)
    && typeof value.keyId === 'string'
    && typeof value.isActive === 'boolean'
    && typeof value.hasPrivateKey === 'boolean'
    && typeof value.algorithm === 'string'
    && typeof value.publicKeyPem === 'string';
}

export function isOidcSigningKeyList(value: unknown): value is OidcSigningKeyList {
  return isRecord(value)
    && typeof value.activeSigningKeyId === 'string'
    && typeof value.usesEphemeralDevelopmentKey === 'boolean'
    && Array.isArray(value.keys)
    && value.keys.every(isOidcSigningKey);
}