import { readProblemDetails } from './problem-details.js';

export interface AuthenticationBridge {
  getAccessToken: () => string | undefined;
  refresh: () => Promise<boolean>;
}

export interface RequestOptions {
  retryUnauthorized?: boolean;
}

export interface HttpClient {
  configureAuthentication(bridge?: AuthenticationBridge): void;
  configureRequestLocale(provider?: () => string): void;
  request<T>(
    path: string,
    init?: RequestInit,
    signal?: AbortSignal,
    options?: RequestOptions
  ): Promise<T>;
}

/** 创建携带凭据的 Full.NET 浏览器 HTTP 客户端；各管理端只注入 API 基址。 */
export function createHttpClient(apiBaseUrl = ''): HttpClient {
  let authentication: AuthenticationBridge | undefined;
  let refreshInFlight: Promise<boolean> | undefined;
  let requestLocale: (() => string) | undefined;

  function configureAuthentication(bridge?: AuthenticationBridge): void {
    authentication = bridge;
    refreshInFlight = undefined;
  }

  function configureRequestLocale(provider?: () => string): void {
    requestLocale = provider;
  }

  async function request<T>(
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

  return {
    configureAuthentication,
    configureRequestLocale,
    request
  };
}
