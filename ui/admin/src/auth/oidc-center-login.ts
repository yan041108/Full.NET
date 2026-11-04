import { http, apiBaseUrl } from '../api/http';
import { resolveAdminOidcClientId } from '../config/identity-auth';
import { rememberLoginDestination } from './login-destination';

let csrfToken: string | undefined;
let generation = 0;

/** BFF 仅允许同源请求；部署使用反向代理，不能把 Cookie 身份扩为任意 CORS 来源。 */
function requireSameOrigin(): void {
  if (apiBaseUrl && new URL(apiBaseUrl, window.location.origin).origin !== window.location.origin) {
    throw new Error('oidc_bff_same_origin_required');
  }
}

export function clearAdminOidcSessionCredentials(): void {
  generation++;
  csrfToken = undefined;
  // 收敛旧 SPA 存储，不读取或迁移其中的 OAuth 凭据。
  sessionStorage.removeItem('fullnet.admin.oidc.pkce');
  sessionStorage.removeItem('fullnet.admin.oidc.refresh');
}

export function readAdminBrowserCsrfToken(): string | undefined { return csrfToken; }

export async function beginAdminOidcCenterLogin(destination?: unknown): Promise<void> {
  requireSameOrigin();
  clearAdminOidcSessionCredentials();
  rememberLoginDestination(destination);
  window.location.assign('/api/v1/identity/browser/login');
}

export async function completeAdminOidcCallback(): Promise<void> {
  requireSameOrigin();
  if (!await adminBrowserSession.restore()) throw new Error('oidc_invalid_callback');
}

export const adminBrowserSession = {
  async restore(): Promise<boolean> {
    requireSameOrigin();
    const operation = generation;
    try {
      const value = await http.request<unknown>('/api/v1/identity/browser/session', {}, undefined, { retryUnauthorized: false });
      if (typeof value !== 'object' || value === null || !('csrfToken' in value)
        || typeof value.csrfToken !== 'string' || value.csrfToken.length < 32
        || 'accessToken' in value || 'refreshToken' in value) return false;
      if (operation !== generation) return false;
      csrfToken = value.csrfToken;
      return true;
    } catch { if (operation === generation) csrfToken = undefined; return false; }
  },
  getCsrfToken: readAdminBrowserCsrfToken,
  async switchTenant(tenantId: string | null): Promise<void> {
    await http.request('/api/v1/tenancy/context', { method: 'PUT', body: JSON.stringify({ tenantId }) });
  },
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await http.request('/api/v1/identity/browser/password', {
      method: 'POST', body: JSON.stringify({ currentPassword, newPassword })
    });
  },
  async logout(): Promise<void> {
    await http.request('/api/v1/identity/browser/logout', { method: 'POST' }, undefined, { retryUnauthorized: false });
    clearAdminOidcSessionCredentials();
    rememberLoginDestination(undefined);
    const parameters = new URLSearchParams({ client_id: resolveAdminOidcClientId(), post_logout_redirect_uri: window.location.origin + '/' });
    window.location.assign('/connect/logout?' + parameters.toString());
  },
  clear: clearAdminOidcSessionCredentials
};
