/** 采集只返回本地路径，上传鉴权仍统一交给 HttpClient。 */
export async function chooseWorkflowAttachment(): Promise<string | undefined> {
  if (typeof uni.chooseFile === 'function') {
    const selected = await uni.chooseFile({ count: 1 });
    return Array.isArray(selected.tempFilePaths) ? selected.tempFilePaths[0] : selected.tempFilePaths;
  }
  if (typeof uni.chooseMessageFile === 'function') {
    return await new Promise((resolve, reject) => uni.chooseMessageFile({ count: 1, type: 'all',
      success: result => resolve(result.tempFiles[0]?.path), fail: reject }));
  }
  // 没有通用文件选择器的平台保留图片采集；不伪造浏览器 File 或远程 URL。
  const selected = await uni.chooseImage({ count: 1, sourceType: ['album', 'camera'] });
  return selected.tempFilePaths[0];
}

/** 浏览器只下载，不在同源窗口执行外来 HTML/SVG；小程序仅交付系统支持的文档类型。 */
export async function deliverWorkflowAttachment(bytes: ArrayBuffer, name: string): Promise<void> {
  const safeName = name.replace(/[\\/\u0000-\u001f\u007f]/g, '_');
  if (typeof document !== 'undefined' && typeof URL.createObjectURL === 'function') {
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    const link = document.createElement('a');
    link.href = url; link.download = safeName; document.body.append(link);
    try { link.click(); } finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1_000); }
    return;
  }
  const extension = safeName.split('.').at(-1)?.toLowerCase();
  const platform = globalThis as typeof globalThis & {
    wx?: { env?: { USER_DATA_PATH?: string } };
    my?: { env?: { USER_DATA_PATH?: string } };
  };
  const directory = platform.wx?.env?.USER_DATA_PATH ?? platform.my?.env?.USER_DATA_PATH;
  if (!directory || !extension || !['doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'pdf'].includes(extension) ||
    typeof uni.getFileSystemManager !== 'function') throw new Error('workflow.file.platform_unavailable');
  const filePath = `${directory}/fullnet-workflow-attachment.${extension}`;
  const files = uni.getFileSystemManager();
  try {
    await new Promise<void>((resolve, reject) => files.writeFile({ filePath, data: bytes, success: () => resolve(), fail: reject }));
    await uni.openDocument({ filePath, fileType: extension, showMenu: true });
  } finally {
    // 文件仅在这次系统查看期间存在，不在设备上保留跨会话附件缓存。
    await new Promise<void>(resolve => files.unlink({ filePath, complete: () => resolve() }));
  }
}
