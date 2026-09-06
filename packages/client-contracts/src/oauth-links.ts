export interface OAuthUserLink {
  id: string;
  providerKey: string;
  providerDisplayName: string;
  subject: string;
  email: string | null;
  emailVerified: boolean;
  displayName: string | null;
  linkedAtUtc: string;
  lastUsedAtUtc: string | null;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isOAuthUserLink(value: unknown): value is OAuthUserLink {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.providerKey === 'string'
    && typeof value.providerDisplayName === 'string'
    && typeof value.subject === 'string'
    && (value.email === null || typeof value.email === 'string')
    && typeof value.emailVerified === 'boolean'
    && (value.displayName === null || typeof value.displayName === 'string')
    && typeof value.linkedAtUtc === 'string'
    && (value.lastUsedAtUtc === null || typeof value.lastUsedAtUtc === 'string');
}

export function isOAuthUserLinkList(value: unknown): value is OAuthUserLink[] {
  return Array.isArray(value) && value.every(isOAuthUserLink);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
