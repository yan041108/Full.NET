/** 以下载方式交付附件，不能在后台同源Blob窗口执行外来HTML/SVG等主动内容。 */
export function openWorkflowFormAttachmentBlob(blob: Blob, name = 'attachment'): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a'); link.href = url;
  link.download = name.replace(/[\\/\u0000-\u001f\u007f]/g, '_');
  document.body.append(link);
  try { link.click(); } finally { link.remove(); window.setTimeout(() => URL.revokeObjectURL(url), 1_000); }
}
