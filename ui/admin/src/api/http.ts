import { readProblemDetails } from '@fullnet/client-contracts';

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';

/** 调用 Full.NET 标准 API，并保留 Cookie 会话、请求取消和稳定错误码。 */
export async function request<T>(
  path: string,
  init: RequestInit = {},
  signal?: AbortSignal
): Promise<T> {
  const headers = new Headers(init.headers);
  if (!headers.has('accept')) {
    headers.set('accept', 'application/json');
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers,
    signal: signal ?? init.signal
  });

  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return await response.json() as T;
}
