import {
  isTokenResponse,
  type TokenResponse
} from './identity.js';

export interface TenantContextSummary {
  id: string;
  identifier: string;
  name: string;
  domain: string;
}

export interface TenantContextDescriptor {
  tenantId: string | null;
  identifier: string;
  name: string;
  scope: string;
}

export interface TenantContextTokenResponse extends TokenResponse {
  context: TenantContextDescriptor;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const identifierPattern = /^[a-z0-9][a-z0-9-]{0,62}$/;

/** 校验可选租户列表，并拒绝会导致选择器键冲突的重复标识。 */
export function isTenantContextSummaryArray(
  value: unknown
): value is TenantContextSummary[] {
  if (!Array.isArray(value)) {
    return false;
  }

  const ids = new Set<string>();
  const identifiers = new Set<string>();
  for (const item of value) {
    if (!isTenantContextSummary(item)
      || ids.has(item.id.toLowerCase())
      || identifiers.has(item.identifier)) {
      return false;
    }

    ids.add(item.id.toLowerCase());
    identifiers.add(item.identifier);
  }

  return true;
}

/** 校验上下文切换返回的新令牌及其有效租户描述。 */
export function isTenantContextTokenResponse(
  value: unknown
): value is TenantContextTokenResponse {
  return isTokenResponse(value)
    && isRecord(value)
    && isTenantContextDescriptor(value.context);
}

function isTenantContextSummary(value: unknown): value is TenantContextSummary {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.identifier === 'string'
    && identifierPattern.test(value.identifier)
    && isNonEmptyString(value.name)
    && isNonEmptyString(value.domain);
}

function isTenantContextDescriptor(
  value: unknown
): value is TenantContextDescriptor {
  if (!isRecord(value)
    || !(value.tenantId === null || isGuid(value.tenantId))
    || !isNonEmptyString(value.identifier)
    || !isNonEmptyString(value.name)
    || !isNonEmptyString(value.scope)) {
    return false;
  }

  if (value.tenantId === null) {
    return value.identifier === 'host' && value.scope === 'host';
  }

  const scopeTenantId = value.tenantId.replaceAll('-', '').toLowerCase();
  return identifierPattern.test(value.identifier)
    && value.scope === `tenant:${scopeTenantId}`;
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
