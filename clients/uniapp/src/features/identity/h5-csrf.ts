/** 从浏览器 Cookie 安全提取 CSRF 双提交令牌。 */
export function readH5CsrfHeaders(
  cookie = typeof document === 'undefined' ? '' : document.cookie
): Readonly<Record<string, string>> {
  const encodedValue = cookie
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
