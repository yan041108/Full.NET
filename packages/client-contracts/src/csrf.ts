/** 从 Cookie 读取 CSRF 令牌并构造写请求头；损坏编码时返回空对象。 */
export function readCsrfHeaders(): HeadersInit {
  const encodedValue = document.cookie
    .split(';')
    .map(part => part.trim())
    .find(part => part.startsWith('fullnet-csrf='))
    ?.slice('fullnet-csrf='.length);
  if (!encodedValue) {
    return {};
  }

  try {
    return { 'X-CSRF-Token': decodeURIComponent(encodedValue) };
  } catch {
    return {};
  }
}
