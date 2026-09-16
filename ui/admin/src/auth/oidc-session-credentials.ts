/** 管理端 OIDC refresh token 的 sessionStorage 键；仅在同标签页内恢复会话。 */
export const ADMIN_OIDC_REFRESH_STORAGE_KEY = 'fullnet.admin.oidc.refresh';

export interface OidcRefreshCredential {
  refreshToken: string;
  clientId: string;
}

function isOidcRefreshCredential(value: unknown): value is OidcRefreshCredential {
  return typeof value === 'object'
    && value !== null
    && typeof (value as OidcRefreshCredential).refreshToken === 'string'
    && (value as OidcRefreshCredential).refreshToken.length > 0
    && typeof (value as OidcRefreshCredential).clientId === 'string'
    && (value as OidcRefreshCredential).clientId.length > 0;
}

/** 读取已持久化的 OIDC refresh token；损坏或缺失时返回 undefined。 */
export function readOidcRefreshCredential(
  storage: Storage = sessionStorage
): OidcRefreshCredential | undefined {
  const raw = storage.getItem(ADMIN_OIDC_REFRESH_STORAGE_KEY);
  if (raw === null) {
    return undefined;
  }

  try {
    const parsed: unknown = JSON.parse(raw);
    return isOidcRefreshCredential(parsed) ? parsed : undefined;
  } catch {
    return undefined;
  }
}

/** 持久化 OIDC refresh token，供页面刷新后恢复会话。 */
export function writeOidcRefreshCredential(
  credential: OidcRefreshCredential,
  storage: Storage = sessionStorage
): void {
  storage.setItem(ADMIN_OIDC_REFRESH_STORAGE_KEY, JSON.stringify(credential));
}

/** 清理 OIDC refresh token，退出或刷新失败时调用。 */
export function clearOidcRefreshCredential(storage: Storage = sessionStorage): void {
  storage.removeItem(ADMIN_OIDC_REFRESH_STORAGE_KEY);
}