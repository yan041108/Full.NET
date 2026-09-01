import {
  filesDeleteHostFile,
  filesDownloadHostFileContent,
  filesListHostFiles,
  filesUploadHostFile,
  type HostFile,
  type HostFilePage
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 文件列表。 */
export async function listHostFiles(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostFilePage> {
  return filesListHostFiles(http, { page, pageSize }, signal);
}

/** 上传 Host 文件。 */
export async function uploadHostFile(
  file: File,
  signal?: AbortSignal
): Promise<HostFile> {
  return filesUploadHostFile(http, { file }, signal);
}

/** 删除指定 Host 文件。 */
export async function deleteHostFile(
  id: string,
  signal?: AbortSignal
): Promise<HostFile> {
  return filesDeleteHostFile(http, { fileId: id }, signal);
}

/** 使用已认证客户端拉取文件内容，避免在 URL 中暴露令牌。 */
export async function downloadHostFileContent(
  id: string,
  signal?: AbortSignal
): Promise<Blob> {
  return filesDownloadHostFileContent(http, { fileId: id }, signal);
}

/** 将已下载 Blob 以短生命周期对象 URL 打开，并在窗口关闭后回收。 */
export function openHostFileBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const opened = window.open(url, '_blank', 'noopener,noreferrer');
  if (!opened) {
    URL.revokeObjectURL(url);
    return;
  }

  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** 导出 Host 文件明细与分页模型，供列表页、上传流程和下载结果复用同一契约。 */
export type { HostFile, HostFilePage };
