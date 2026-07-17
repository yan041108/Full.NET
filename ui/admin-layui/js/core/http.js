const apiBaseUrl = globalThis.FULLNET_CONFIG?.apiBaseUrl ?? '';
let authentication;
let refreshInFlight;

/** 注入内存令牌和刷新行为；无参数调用用于测试或退出时重置。 */
export function configureAuthentication(bridge) {
  authentication = bridge;
  refreshInFlight = undefined;
}

/** 解析不可信错误响应，并把代理页面或损坏 JSON 降级为稳定错误。 */
async function readProblemDetails(response) {
  const contentType = response.headers.get('content-type')?.toLowerCase() ?? '';
  if (contentType.includes('json')) {
    try {
      const value = await response.clone().json();
      if (Number.isInteger(value?.status) && typeof value?.code === 'string' && value.code.length > 0) {
        return value;
      }
    } catch {
      // 客户端不能把第三方代理产生的 JSON 解析异常暴露为业务错误。
    }
  }

  return {
    status: response.status,
    code: 'http.unexpected_response',
    title: response.statusText || '请求失败'
  };
}

/** 调用 Full.NET 标准 API，并在 401 时协调一次去重刷新。 */
export async function request(path, init = {}, signal, options = {}) {
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
      return await request(path, init, signal, { retryUnauthorized: false });
    }
  }

  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (response.status === 204) {
    return undefined;
  }

  return await response.json();
}

async function send(path, init, signal) {
  const headers = new Headers(init.headers);
  if (!headers.has('accept')) {
    headers.set('accept', 'application/json');
  }

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
