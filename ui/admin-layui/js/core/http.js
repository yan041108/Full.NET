const apiBaseUrl = globalThis.FULLNET_CONFIG?.apiBaseUrl ?? '';

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

/** 调用 Full.NET 标准 API；浏览器会话由安全 Cookie 和内存态共同维护。 */
export async function request(path, init = {}, signal) {
  const headers = new Headers(init.headers);
  if (!headers.has('accept')) {
    headers.set('accept', 'application/json');
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: 'include',
    headers,
    signal
  });
  if (!response.ok) {
    throw await readProblemDetails(response);
  }

  if (response.status === 204) {
    return undefined;
  }

  return await response.json();
}
