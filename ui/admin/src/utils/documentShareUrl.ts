/** 根据分享码生成管理端 Hash 路由下的公开访问链接。 */
export function buildDocumentShareUrl(shareCode: string): string {
  const code = shareCode.trim();
  if (!code) {
    return '';
  }
  const pageBase = window.location.href.split('#')[0].replace(/\/$/, '');
  return `${pageBase}#/document/share/${encodeURIComponent(code)}`;
}
