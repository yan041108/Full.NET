import { isRecord, isInteger, type PagedResult } from '@fullnet/client-contracts';
import {
  documentHostAddItemVersion,
  documentHostItempublish,
  documentHostItemarchive,
  documentHostItemunarchive,
  documentHostCreateItem,
  documentHostDeleteItem,
  documentHostDownloadItemContent,
  documentHostListItems,
  documentHostGetItem,
  documentHostListItemVersions,
  documentHostPreviewItemContent,
  documentHostPreviewItemVersionContent,
  documentHostRestoreItem,
  documentHostRollbackItemVersion,
  documentHostDeleteItemVersion,
  documentHostUpdateItem,
  documentHostUploadItemVersion,
  isHostDocumentItemPage,
  isHostDocumentItemResponse,
  isHostDocumentVersionList,
  type HostDocumentItemPage,
  type HostDocumentItemResponse,
  type HostDocumentVersionResponse
} from '@fullnet/client-contracts';
import { http } from './http';
import { openContentBlob } from '../utils/openContentBlob';

// 同一文件对象的失败重试保留操作身份，重新选择文件会获得新的身份。
const uploadKeys = new WeakMap<File, Map<string, string>>();
function uploadRequestKey(itemId: string, file: File): string {
  let keys = uploadKeys.get(file);
  if (!keys) { keys = new Map(); uploadKeys.set(file, keys); }
  let key = keys.get(itemId);
  if (!key) { key = crypto.randomUUID(); keys.set(itemId, key); }
  return key;
}

export interface ListDocumentItemsOptions {
  readonly keyword?: string;
  readonly categoryId?: string | null;
  readonly status?: number;
  readonly documentType?: number;
  readonly tagId?: string | null;
}

/** 搜索详情直接按对象重新读取，不能依赖上一次列表中的授权或对象快照。 */
export async function getDocumentItem(itemId: string, signal?: AbortSignal): Promise<HostDocumentItemResponse> {
  const value = await documentHostGetItem(http, { itemId }, signal);
  if (!isHostDocumentItemResponse(value) || value.id.toLowerCase() !== itemId.toLowerCase()) throw new Error('client.invalid_document_item');
  return value;
}

/** 分页查询文档项列表，并对响应页做失败关闭校验。 */
export async function listDocumentItems(
  page = 1,
  pageSize = 20,
  options: ListDocumentItemsOptions = {},
  signal?: AbortSignal
): Promise<HostDocumentItemPage> {
  const value = await documentHostListItems(
    http,
    {
      page,
      pageSize,
      tagId: options.tagId ?? undefined,
      keyword: options.keyword || undefined, categoryId: options.categoryId ?? undefined,
      status: options.status, documentType: options.documentType
    },
    signal
  );
  if (!isHostDocumentItemPage(value)) {
    throw new Error('client.invalid_document_item_page');
  }
  return value;
}

export interface CreateDocumentItemOptions {
  readonly categoryId?: string | null;
  readonly tagIds?: readonly string[] | null;
}

/** 创建文档项。 */
export async function createDocumentItem(
  title: string,
  description: string | null,
  options: CreateDocumentItemOptions = {},
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostCreateItem>[1]['body'] = {
    title,
    description,
    categoryId: options.categoryId ?? null,
    documentType: 1,
    sort: 0,
    status: 0,
    tagIds: options.tagIds ? [...options.tagIds] : null,
    thumbnail: null
  };
  const value = await documentHostCreateItem(http, { body }, signal);
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export interface UpdateDocumentItemOptions {
  readonly categoryId?: string | null;
  readonly thumbnail?: string | null;
  readonly sort?: number;
  readonly tagIds?: readonly string[] | null;
}

/** 更新文档项基础信息，并携带版本号维持乐观并发。 */
export async function updateDocumentItem(
  item: HostDocumentItemResponse,
  title: string,
  description: string | null,
  options: UpdateDocumentItemOptions = {},
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostUpdateItem>[1]['body'] = {
    title,
    description,
    version: item.version,
    categoryId: options.categoryId === undefined ? item.categoryId : options.categoryId,
    sort: options.sort ?? item.sort,
    thumbnail: options.thumbnail === undefined ? item.thumbnail : options.thumbnail,
    tagIds: options.tagIds ? [...options.tagIds] : item.tags.map(tag => tag.tagId)
  };
  const value = await documentHostUpdateItem(
    http,
    {
      itemId: item.id,
      body
    },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

/** 直接上传新版本文件并附着到文档项。 */
export async function uploadDocumentVersion(
  itemId: string,
  file: File,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const value = await documentHostUploadItemVersion(
    http,
    { itemId, file, requestKey: uploadRequestKey(itemId, file) },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

/** 下载文档项当前内容。 */
export async function downloadDocumentContent(
  itemId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return documentHostDownloadItemContent(http, { itemId, requestKey: crypto.randomUUID() }, signal);
}

/** 查询文档项的历史版本分页。 */
export async function listDocumentVersions(
  itemId: string,
  signal?: AbortSignal, page = 1, pageSize = 20
): Promise<PagedResult<HostDocumentVersionResponse>> {
  const value = await documentHostListItemVersions(http, { itemId, page, pageSize }, signal);
  if (!isRecord(value) || !isHostDocumentVersionList(value.items) || !isInteger(value.page) || !isInteger(value.pageSize) || typeof value.total !== 'number') {
    throw new Error('client.invalid_document_version_list');
  }
  return value as unknown as PagedResult<HostDocumentVersionResponse>;
}

/** 预览文档内容；传入 versionId 时预览指定历史版本，否则预览当前版本。 */
export async function previewDocumentContent(
  itemId: string,
  versionId?: string,
  signal?: AbortSignal
): Promise<Blob> {
  if (versionId) {
    return documentHostPreviewItemVersionContent(
      http,
      { itemId, versionId, requestKey: crypto.randomUUID() },
      signal
    );
  }

  return documentHostPreviewItemContent(http, { itemId, requestKey: crypto.randomUUID() }, signal);
}

/** 活动文本与 SVG 仅按纯文本展示，不能在后台同源 Blob 中执行脚本。 */
export function safeDocumentPreviewBlob(blob: Blob): Blob {
  const mime = blob.type.split(';', 1)[0]?.trim().toLowerCase() ?? '';
  return mime.startsWith('text/') || mime === 'image/svg+xml' || mime === 'application/xhtml+xml'
    ? blob.slice(0, blob.size, 'text/plain') : blob;
}

/** 复用文档/文件一致的内容策略与有界对象 URL 生命周期。 */
export function openDocumentBlob(blob: Blob, fileName?: string): void { openContentBlob(blob, fileName); }

/** 将已上传文件追加为文档项的新版本。 */
export async function addDocumentVersion(
  itemId: string,
  fileId: string,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostAddItemVersion>[1]['body'] = {
    fileId,
    requestKey: crypto.randomUUID(),
    changeDescription: null
  };
  const value = await documentHostAddItemVersion(
    http,
    {
      itemId,
      body
    },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

/** 删除文档项。 */
export async function deleteDocumentItem(
  itemId: string,
  version: number,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostDeleteItem(
    http,
    {
      itemId,
      body: { version }
    },
    signal
  );
}

/** 从回收站恢复文档项。 */
export async function restoreDocumentItem(
  itemId: string,
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const value = await documentHostRestoreItem(
    http,
    {
      itemId,
      body: { version }
    },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  if (value.id.toLowerCase() !== itemId.toLowerCase()) throw new Error('client.invalid_document_item_identity');
  return value;
}

/** 将文档当前版本指针回滚到既有历史版本。 */
export async function rollbackDocumentVersion(
  itemId: string,
  versionId: string,
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const value = await documentHostRollbackItemVersion(
    http,
    {
      itemId,
      versionId,
      body: { version }
    },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  if (value.id.toLowerCase() !== itemId.toLowerCase()) throw new Error('client.invalid_document_item_identity');
  return value;
}

/** 删除非当前的历史版本。 */
export async function deleteDocumentVersion(
  itemId: string,
  versionId: string,
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const value = await documentHostDeleteItemVersion(
    http,
    {
      itemId,
      versionId,
      body: { version }
    },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  if (value.id.toLowerCase() !== itemId.toLowerCase()) throw new Error('client.invalid_document_item_identity');
  return value;
}

/** 导出文档项分页、详情与版本模型，供列表页、版本侧栏与内容预览流程共享同一契约。 */
export type { HostDocumentItemPage, HostDocumentItemResponse, HostDocumentVersionResponse };

/** 显式状态命令由服务端按精确权限及并发版本验证。 */
export async function changeDocumentStatus(item: HostDocumentItemResponse, action: 'publish' | 'archive' | 'unarchive', signal?: AbortSignal): Promise<HostDocumentItemResponse> {
  const command = { publish: documentHostItempublish, archive: documentHostItemarchive, unarchive: documentHostItemunarchive }[action];
  const value = await command(http, { itemId: item.id, body: { version: item.version } }, signal);
  if (!isHostDocumentItemResponse(value)) throw new Error('client.invalid_document_item');
  return value;
}
