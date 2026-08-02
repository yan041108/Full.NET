import {
  isHostFile,
  isHostFilePage,
  type HostFile,
  type HostFilePage
} from '@fullnet/client-contracts';
import { request, requestBlob } from './http';

export async function listHostFiles(
  page = 1,
  pageSize = 20
): Promise<HostFilePage> {
  const value = await request<unknown>(
    `/api/v1/files/host-files?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostFilePage(value)) {
    throw new Error('client.invalid_host_file_page');
  }
  return value;
}

export async function uploadHostFile(file: File): Promise<HostFile> {
  const formData = new FormData();
  formData.append('file', file);
  const value = await request<unknown>('/api/v1/files/host-files', {
    method: 'POST',
    body: formData
  });
  if (!isHostFile(value)) {
    throw new Error('client.invalid_host_file');
  }
  return value;
}

export async function deleteHostFile(id: string): Promise<HostFile> {
  const value = await request<unknown>(
    `/api/v1/files/host-files/${encodeURIComponent(id)}/delete`,
    { method: 'POST' }
  );
  if (!isHostFile(value)) {
    throw new Error('client.invalid_host_file');
  }
  return value;
}

/** 使用已认证客户端拉取文件内容，避免在 URL 中暴露令牌。 */
export async function downloadHostFileContent(id: string): Promise<Blob> {
  return requestBlob(
    `/api/v1/files/host-files/${encodeURIComponent(id)}/content`
  );
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
