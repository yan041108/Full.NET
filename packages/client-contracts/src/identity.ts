export interface TokenResponse {
  accessToken: string;
  tokenType: 'Bearer';
  expiresAtUtc: string;
}

export interface CurrentUserResponse {
  id: string;
  username: string;
  displayName: string;
  tenantId: string | null;
  scope: string;
  permissions: string[];
  sessionId: string;
}

/** 校验不可信 JSON 是否符合访问令牌响应的最小契约。 */
export function isTokenResponse(value: unknown): value is TokenResponse {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.accessToken === 'string'
    && value.accessToken.length > 0
    && value.tokenType === 'Bearer'
    && typeof value.expiresAtUtc === 'string'
    && value.expiresAtUtc.length > 0;
}

/** 校验不可信 JSON 是否为可安全展示的当前用户摘要。 */
export function isCurrentUserResponse(value: unknown): value is CurrentUserResponse {
  if (!isRecord(value)) {
    return false;
  }

  return typeof value.id === 'string'
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && (typeof value.tenantId === 'string' || value.tenantId === null)
    && typeof value.scope === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every(permission => typeof permission === 'string')
    && typeof value.sessionId === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
