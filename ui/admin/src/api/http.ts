import { readProblemDetails } from '@fullnet/client-contracts';
import type { SupportedLocale } from '@fullnet/admin-i18n';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

interface AuthenticationBridge {
  getAccessToken: () => string | undefined;
  refresh: () => Promise<boolean>;
}

interface RequestOptions {
  retryUnauthorized?: boolean;
}

let authentication: AuthenticationBridge | undefined;
let refreshInFlight: Promise<boolean> | undefined;
let requestLocale: (() => SupportedLocale) | undefined;

/** 注入内存令牌和刷新行为；无参数调用用于测试或退出时重置。 */
export function configureAuthentication(bridge?: AuthenticationBridge): void {
  authentication = bridge;
  refreshInFlight = undefined;
}

/** 注入每次发送前读取的活动语言；无参数调用用于测试隔离。 */
export function configureRequestLocale(
  provider?: () => SupportedLocale
): void {
  requestLocale = provider;
}

/** 调用 Full.NET 标准 API，并在 401 时协调一次去重刷新。 */
export async function request<T>(
  path: string,
  init: RequestInit = {},
  signal?: AbortSignal,
  options: RequestOptions = {}
): Promise<T> {
  const response = await send(path, init, signal);
  const authenticationBridge = authentication;
  const shouldRetry = options.retryUnauthorized !== false
    && response.status === 401
    && authenticationBridge !== undefined;
  if (shouldRetry) {
    refreshInFlight ??= authenticationBridge.refresh().finally(() => {
      refreshInFlight = undefined;
    });
    if (await refreshInFlight) {
      return await request<T>(path, init, signal, { retryUnauthorized: false });
    }
  }

  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return await response.json() as T;
}

async function send(
  path: string,
  init: RequestInit,
  signal?: AbortSignal
): Promise<Response> {
  const headers = new Headers(init.headers);
  if (!headers.has('accept')) {
    headers.set('accept', 'application/json');
  }
  headers.set('accept-language', requestLocale?.() ?? 'zh-CN');

  const accessToken = authentication?.getAccessToken();
  if (accessToken && !headers.has('authorization')) {
    headers.set('authorization', `Bearer ${accessToken}`);
  }

  return await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers,
    signal: signal ?? init.signal
  });
}
