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
  actorScope: string;
  scope: string;
  permissions: string[];
  sessionId: string;
  preferredLocale: SupportedLocale;
  profileVersion: number;
}

export type SupportedLocale = 'zh-CN' | 'en-US';

export interface LocalePreferenceResponse {
  preferredLocale: SupportedLocale;
  profileVersion: number;
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
    && value.expiresAtUtc.length > 0
    // 语言偏好只能来自认证后的 /me，禁止把可变资料混入令牌生命周期。
    && !('preferredLocale' in value)
    && !('profileVersion' in value);
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
    && typeof value.actorScope === 'string'
    && value.actorScope.length > 0
    && typeof value.scope === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every(permission => typeof permission === 'string')
    && typeof value.sessionId === 'string'
    && isSupportedLocale(value.preferredLocale)
    && isPositiveInteger(value.profileVersion);
}

/** 校验语言偏好更新响应，避免损坏响应造成客户端乐观切换。 */
export function isLocalePreferenceResponse(
  value: unknown
): value is LocalePreferenceResponse {
  return isRecord(value)
    && isSupportedLocale(value.preferredLocale)
    && isPositiveInteger(value.profileVersion);
}

function isSupportedLocale(value: unknown): value is SupportedLocale {
  return value === 'zh-CN' || value === 'en-US';
}

function isPositiveInteger(value: unknown): value is number {
  return Number.isInteger(value) && Number(value) > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
