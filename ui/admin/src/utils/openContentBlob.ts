/** 只允许浏览器以被动格式预览；HTML、SVG、XML 与未知类型不能直接作为应用来源文档打开。 */
export function isPassiveContentType(contentType: string | null | undefined): boolean {
  const type = contentType?.split(';', 1)[0]?.trim().toLowerCase();
  return type !== undefined && ['application/pdf', 'text/plain', 'image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/avif', 'image/bmp'].includes(type);
}

/** 未提供原名时仅为已知格式保留可用扩展名，不把 MIME 推导名冒充源文件名。 */
function defaultFileName(type: string): string {
  const extensions: Record<string, string> = {
    'application/msword': 'doc', 'application/vnd.ms-excel': 'xls', 'application/vnd.ms-powerpoint': 'ppt',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document': 'docx',
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': 'xlsx',
    'application/vnd.openxmlformats-officedocument.presentationml.presentation': 'pptx',
    'application/zip': 'zip', 'application/x-zip-compressed': 'zip'
  };
  const key = type.split(';', 1)[0]?.trim().toLowerCase() ?? '';
  return 'document.' + (Object.hasOwn(extensions, key) ? extensions[key] : 'bin');
}

/** 主动及未知内容按二进制下载；一分钟后调度 URL 回收，不凭 noopener 的返回句柄判断打开成功。 */
export function openContentBlob(blob: Blob, fileName = defaultFileName(blob.type)): void {
  const passive = isPassiveContentType(blob.type);
  const url = URL.createObjectURL(passive ? blob : new Blob([blob], { type: 'application/octet-stream' }));
  let delivered = false;
  try {
    if (passive) window.open(url, '_blank', 'noopener,noreferrer');
    else {
      const anchor = document.createElement('a'); anchor.href = url;
      anchor.download = fileName.split(/[\\/]/).pop()?.replace(/[\u0000-\u001f\u007f]/g, '') || defaultFileName(blob.type);
      anchor.click();
    }
    delivered = true;
  } finally {
    if (delivered) window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
    else URL.revokeObjectURL(url);
  }
}
