export interface OidcSigningKey {
  keyId: string;
  isActive: boolean;
  hasPrivateKey: boolean;
  algorithm: string;
  publicKeyPem: string;
  lifecycleState?: 'unregistered' | 'prepublished' | 'active' | 'retained' | 'retired';
  publicKeyHash?: string | null;
  publishedAtUtc?: string | null;
  retireAfterUtc?: string | null;
  isActivationReady?: boolean;
  isRetirementReady?: boolean;
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
    && typeof value.publicKeyPem === 'string'
    && (value.lifecycleState === undefined || typeof value.lifecycleState === 'string' && ['unregistered', 'prepublished', 'active', 'retained', 'retired'].includes(value.lifecycleState))
    && (value.publicKeyHash == null || typeof value.publicKeyHash === 'string')
    && (value.publishedAtUtc == null || typeof value.publishedAtUtc === 'string')
    && (value.retireAfterUtc == null || typeof value.retireAfterUtc === 'string')
    && (value.isActivationReady === undefined || typeof value.isActivationReady === 'boolean')
    && (value.isRetirementReady === undefined || typeof value.isRetirementReady === 'boolean');
}

export function isOidcSigningKeyList(value: unknown): value is OidcSigningKeyList {
  return isRecord(value)
    && typeof value.activeSigningKeyId === 'string'
    && typeof value.usesEphemeralDevelopmentKey === 'boolean'
    && Array.isArray(value.keys)
    && value.keys.every(isOidcSigningKey);
}
