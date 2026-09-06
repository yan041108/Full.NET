import {
  filesCreateHostFolder,
  filesDeleteHostFile,
  filesDeleteHostFolder,
  filesDownloadHostFileContent,
  filesGetHostFolderTree,
  filesListHostFileReferences,
  filesListHostFiles,
  filesUpdateHostFileMetadata,
  filesUpdateHostFolder,
  filesUploadHostFile,
  type BatchDeleteHostFilesResponse,
  type BatchUploadHostFilesResponse,
  type HostFile,
  type HostFilePage,
  type HostFileReferenceClaimResponse,
  type HostFolderResponse,
  type HostFolderTreeNode,
  type PagedResultOfHostFileReferenceClaimResponse
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询 Host 文件列表。 */
export async function listHostFiles(
  page = 1,
  pageSize = 20,
  options?: {
    folderId?: string;
    fileNameContains?: string;
  },
  signal?: AbortSignal
): Promise<HostFilePage> {
  return filesListHostFiles(
    http,
    {
      page,
      pageSize,
      folderId: options?.folderId,
      fileNameContains: options?.fileNameContains
    },
    signal
  );
}

/** 上传 Host 文件。 */
export async function uploadHostFile(
  file: File,
  folderId?: string,
  signal?: AbortSignal
): Promise<HostFile> {
  return filesUploadHostFile(http, { file, folderId }, signal);
}

/** 批量上传 Host 文件并返回逐条结果。 */
export async function batchUploadHostFiles(
  files: File[],
  folderId?: string,
  signal?: AbortSignal
): Promise<BatchUploadHostFilesResponse> {
  const body = new FormData();
  for (const file of files) {
    body.append('files', file, file.name);
  }
  if (folderId) {
    body.append('folderId', folderId);
  }

  return http.request<BatchUploadHostFilesResponse>(
    '/api/v1/files/host-files/batch-upload',
    { method: 'POST', body },
    signal
  );
}

/** 批量删除 Host 文件并返回逐条结果。 */
export async function batchDeleteHostFiles(
  fileIds: string[],
  signal?: AbortSignal
): Promise<BatchDeleteHostFilesResponse> {
  return http.request<BatchDeleteHostFilesResponse>(
    '/api/v1/files/host-files/batch-delete',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fileIds })
    },
    signal
  );
}

/** 拉取可安全预览的文件内容。 */
export async function previewHostFileContent(
  id: string,
  signal?: AbortSignal
): Promise<Blob> {
  return http.requestBlob(
    `/api/v1/files/host-files/${id}/preview`,
    { method: 'GET' },
    signal
  );
}

/** 更新 Host 文件元数据。 */
export async function updateHostFileMetadata(
  fileId: string,
  body: {
    expectedRevision: number;
    originalFileName: string;
    folderId: string | null;
  },
  signal?: AbortSignal
): Promise<HostFile> {
  return filesUpdateHostFileMetadata(http, { fileId, body }, signal);
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

/** 查询 Host 虚拟目录树。 */
export async function listHostFolderTree(
  signal?: AbortSignal
): Promise<HostFolderTreeNode[]> {
  return filesGetHostFolderTree(http, {}, signal);
}

/** 创建 Host 虚拟目录。 */
export async function createHostFolder(
  body: {
    parentId?: string | null;
    name: string;
    displayOrder?: number;
  },
  signal?: AbortSignal
): Promise<HostFolderResponse> {
  return filesCreateHostFolder(http, { body }, signal);
}

/** 更新 Host 虚拟目录。 */
export async function updateHostFolder(
  folderId: string,
  body: {
    expectedRevision: number;
    name: string;
    displayOrder: number;
  },
  signal?: AbortSignal
): Promise<HostFolderResponse> {
  return filesUpdateHostFolder(http, { folderId, body }, signal);
}

/** 删除 Host 虚拟目录。 */
export async function deleteHostFolder(
  folderId: string,
  body: { expectedRevision: number },
  signal?: AbortSignal
): Promise<HostFolderResponse> {
  return filesDeleteHostFolder(http, { folderId, body }, signal);
}

/** 分页查询文件引用声明。 */
export async function listHostFileReferences(
  fileId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfHostFileReferenceClaimResponse> {
  return filesListHostFileReferences(http, { fileId, page, pageSize }, signal);
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

export type {
  HostFile,
  HostFilePage,
  HostFolderTreeNode,
  HostFolderResponse,
  HostFileReferenceClaimResponse,
  PagedResultOfHostFileReferenceClaimResponse,
  BatchUploadHostFilesResponse,
  BatchDeleteHostFilesResponse
};
