export interface CryptographyStatus {
  algorithm: string;
  defaultUserId: string;
  signingPurpose: string;
  deploymentNotice: string;
}

export interface CryptographyKey {
  id: string;
  keyKey: string;
  displayName: string;
  description: string | null;
  algorithm: string;
  purpose: string;
  publicKeyHex: string;
  publicKeyFingerprint: string;
  status: string;
  privateKeyConfigured: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface Sm2SignRequest {
  keyKey: string;
  message: string;
  userId?: string | null;
}

export interface Sm2SignResponse {
  keyId: string;
  keyKey: string;
  algorithm: string;
  signatureHex: string;
  publicKeyFingerprint: string;
  signedAtUtc: string;
}

export interface Sm2VerifyRequest {
  keyKey: string;
  message: string;
  signatureHex: string;
  userId?: string | null;
}

export interface Sm2VerifyResponse {
  keyId: string;
  keyKey: string;
  isValid: boolean;
  publicKeyFingerprint: string;
}

export function isCryptographyStatus(value: unknown): value is CryptographyStatus {
  return isRecord(value)
    && typeof value.algorithm === 'string'
    && typeof value.defaultUserId === 'string'
    && typeof value.signingPurpose === 'string'
    && typeof value.deploymentNotice === 'string';
}

export function isCryptographyKey(value: unknown): value is CryptographyKey {
  return isRecord(value)
    && isNonEmptyString(value.id)
    && isNonEmptyString(value.keyKey)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && isNonEmptyString(value.algorithm)
    && isNonEmptyString(value.purpose)
    && isNonEmptyString(value.publicKeyHex)
    && isNonEmptyString(value.publicKeyFingerprint)
    && isNonEmptyString(value.status)
    && typeof value.privateKeyConfigured === 'boolean'
    && typeof value.sortOrder === 'number'
    && isNonEmptyString(value.createdAtUtc)
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}

export function isSm2SignResponse(value: unknown): value is Sm2SignResponse {
  return isRecord(value)
    && isNonEmptyString(value.keyId)
    && isNonEmptyString(value.keyKey)
    && isNonEmptyString(value.algorithm)
    && isNonEmptyString(value.signatureHex)
    && isNonEmptyString(value.publicKeyFingerprint)
    && isNonEmptyString(value.signedAtUtc);
}

export function isSm2VerifyResponse(value: unknown): value is Sm2VerifyResponse {
  return isRecord(value)
    && isNonEmptyString(value.keyId)
    && isNonEmptyString(value.keyKey)
    && typeof value.isValid === 'boolean'
    && isNonEmptyString(value.publicKeyFingerprint);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
