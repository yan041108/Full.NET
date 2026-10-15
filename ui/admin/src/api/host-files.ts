import {
  isHostFile,
  isHostFilePage,
  filesBatchDeleteHostFiles,
  filesBatchUploadHostFiles,
  filesCreateHostFolder,
  filesDeleteHostFile,
  filesDeleteHostFolder,
  filesDownloadHostFileContent,
  filesGetHostFolderTree,
  filesListHostFileReferences,
  filesListHostFiles,
  filesPreviewHostFileContent,
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
import { openContentBlob } from '../utils/openContentBlob';

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
  const value = await filesListHostFiles(
    http,
    {
      page,
      pageSize,
      folderId: options?.folderId,
      fileNameContains: options?.fileNameContains
    },
    signal
  );
  if (!isHostFilePage(value)) throw new Error('client.invalid_host_file_page');
  return value;
}

/** 上传 Host 文件。 */
export async function uploadHostFile(
  file: File,
  folderId?: string,
  signal?: AbortSignal
): Promise<HostFile> {
  const value = await filesUploadHostFile(http, { file, folderId }, signal);
  if (!isHostFile(value)) throw new Error('client.invalid_host_file_response');
  if (value.sizeBytes !== file.size) throw new Error('client.invalid_host_file_upload');
  return value;
}

/** 批量上传 Host 文件并返回逐条结果。 */
export async function batchUploadHostFiles(
  files: File[],
  folderId?: string,
  signal?: AbortSignal
): Promise<BatchUploadHostFilesResponse> {
  const sources = [...files];
  const result = await filesBatchUploadHostFiles(http, { files: sources, folderId }, signal);
  // 服务端按输入顺序返回；同名文件也必须保留各自的位置与成功载荷。
  const ids = new Set<string>();
  if (result.results.length !== sources.length || result.succeededCount !== result.results.filter(item => item.succeeded).length) throw new Error('client.invalid_host_file_upload_batch');
  for (const [index, item] of result.results.entries()) {
    if (item.originalFileName !== sources[index]!.name || item.succeeded !== (item.file !== null)) throw new Error('client.invalid_host_file_upload_batch');
    if (item.file) {
      const id = item.file.id.toLowerCase();
      if (ids.has(id) || item.file.sizeBytes !== sources[index]!.size) throw new Error('client.invalid_host_file_upload_batch');
      ids.add(id);
    }
  }
  return result;
}

/** 批量删除 Host 文件并返回逐条结果。 */
export async function batchDeleteHostFiles(
  fileIds: string[],
  signal?: AbortSignal
): Promise<BatchDeleteHostFilesResponse> {
  const ids = [...fileIds];
  const result = await filesBatchDeleteHostFiles(http, { body: { fileIds: ids } }, signal);
  // 服务端逐请求项返回；按多重集合核对身份，合法乱序与 UUID 大小写差异不改变归属。
  const remaining = new Map<string, number>();
  for (const id of ids) remaining.set(id.toLowerCase(), (remaining.get(id.toLowerCase()) ?? 0) + 1);
  let succeeded = 0;
  if (result.results.length !== ids.length) throw new Error('client.invalid_host_file_delete_batch');
  for (const item of result.results) {
    const id = item.fileId.toLowerCase(), count = remaining.get(id) ?? 0;
    if (!count) throw new Error('client.invalid_host_file_delete_batch');
    remaining.set(id, count - 1); if (item.succeeded) succeeded++;
  }
  if (succeeded !== result.succeededCount) throw new Error('client.invalid_host_file_delete_batch');
  return result;
}

/** 拉取可安全预览的文件内容。 */
export async function previewHostFileContent(
  id: string,
  signal?: AbortSignal
): Promise<Blob> {
  return filesPreviewHostFileContent(http, { fileId: id }, signal);
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
  const request = { ...body };
  const value = await filesUpdateHostFileMetadata(http, { fileId, body: request }, signal);
  if (!isHostFile(value)) throw new Error('client.invalid_host_file_response');
  // 写后查询可能观察到后续提交，因此允许更高修订，不要求恰好递增一次。
  if (value.id.toLowerCase() !== fileId.toLowerCase() || value.revision <= request.expectedRevision) throw new Error('client.invalid_host_file_mutation');
  return value;
}

/** 删除指定 Host 文件。 */
export async function deleteHostFile(
  id: string,
  signal?: AbortSignal
): Promise<HostFile> {
  const value = await filesDeleteHostFile(http, { fileId: id }, signal);
  if (!isHostFile(value)) throw new Error('client.invalid_host_file_response');
  if (value.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_host_file_identity');
  return value;
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
  const request = { ...body, parentId: body.parentId ?? null, displayOrder: body.displayOrder ?? 0 };
  const value = await filesCreateHostFolder(http, { body: request }, signal);
  if (value.parentId?.toLowerCase() !== request.parentId?.toLowerCase()) throw new Error('client.invalid_host_folder_parent');
  return value;
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
  const request = { ...body };
  const value = await filesUpdateHostFolder(http, { folderId, body: request }, signal);
  if (value.id.toLowerCase() !== folderId.toLowerCase() || value.revision <= request.expectedRevision) throw new Error('client.invalid_host_folder_mutation');
  return value;
}

/** 删除 Host 虚拟目录。 */
export async function deleteHostFolder(
  folderId: string,
  body: { expectedRevision: number },
  signal?: AbortSignal
): Promise<HostFolderResponse> {
  const request = { ...body };
  const value = await filesDeleteHostFolder(http, { folderId, body: request }, signal);
  // 删除返回删除前的快照，不是软删除后的递增修订。
  if (value.id.toLowerCase() !== folderId.toLowerCase() || value.revision !== request.expectedRevision) throw new Error('client.invalid_host_folder_mutation');
  return value;
}

/** 分页查询文件引用声明。 */
export async function listHostFileReferences(
  fileId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedResultOfHostFileReferenceClaimResponse> {
  const result = await filesListHostFileReferences(http, { fileId, page, pageSize }, signal);
  if (result.page !== page || result.pageSize !== pageSize) throw new Error('client.invalid_host_file_reference_page');
  return result;
}

/** 复用文档/文件一致的内容策略与有界对象 URL 生命周期。 */
export function openHostFileBlob(blob: Blob, fileName?: string): void { openContentBlob(blob, fileName); }

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
