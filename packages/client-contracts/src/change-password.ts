import { readCsrfHeaders } from './csrf.js';
import type { HttpClient } from './http.js';
import { isTokenResponse, type TokenResponse } from './identity.js';

/** 当前用户自助改密并轮换当前设备会话令牌。 */
export async function changePassword(
  http: HttpClient,
  currentPassword: string,
  newPassword: string,
  signal?: AbortSignal
): Promise<TokenResponse> {
  const value = await http.request<unknown>(
    '/api/v1/me/password',
    {
      method: 'POST',
      headers: readCsrfHeaders(),
      body: JSON.stringify({ currentPassword, newPassword })
    },
    signal,
    { retryUnauthorized: false }
  );
  if (!isTokenResponse(value)) {
    throw new TypeError('改密响应不符合 TokenResponse 契约。');
  }

  return value;
}
