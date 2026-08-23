import {
  documentHostAddItemVersion,
  documentHostCreateItem,
  documentHostDeleteItem,
  documentHostDownloadItemContent,
  documentHostListItems,
  documentHostListItemVersions,
  documentHostPreviewItemContent,
  documentHostPreviewItemVersionContent,
  documentHostRestoreItem,
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

export async function listDocumentItems(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostDocumentItemPage> {
  const value = await documentHostListItems(http, { page, pageSize }, signal);
  if (!isHostDocumentItemPage(value)) {
    throw new Error('client.invalid_document_item_page');
  }
  return value;
}

export async function createDocumentItem(
  title: string,
  description: string | null,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostCreateItem>[1]['body'] = {
    title,
    description,
    categoryId: null,
    documentType: 1,
    sort: 0,
    status: 1,
    tagIds: null,
    thumbnail: null
  };
  const value = await documentHostCreateItem(http, { body }, signal);
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function updateDocumentItem(
  itemId: string,
  title: string,
  description: string | null,
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostUpdateItem>[1]['body'] = {
    title,
    description,
    version,
    categoryId: null,
    sort: null,
    status: null,
    tagIds: null,
    thumbnail: null
  };
  const value = await documentHostUpdateItem(
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

export async function uploadDocumentVersion(
  itemId: string,
  file: File,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const value = await documentHostUploadItemVersion(
    http,
    { itemId, file },
    signal
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function downloadDocumentContent(
  itemId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return documentHostDownloadItemContent(http, { itemId }, signal);
}

export async function listDocumentVersions(
  itemId: string,
  signal?: AbortSignal
): Promise<HostDocumentVersionResponse[]> {
  const value = await documentHostListItemVersions(http, { itemId }, signal);
  if (!isHostDocumentVersionList(value)) {
    throw new Error('client.invalid_document_version_list');
  }
  return value;
}

export async function previewDocumentContent(
  itemId: string,
  versionId?: string,
  signal?: AbortSignal
): Promise<Blob> {
  if (versionId) {
    return documentHostPreviewItemVersionContent(
      http,
      { itemId, versionId },
      signal
    );
  }

  return documentHostPreviewItemContent(http, { itemId }, signal);
}

/** 将已下载 Blob 以短生命周期对象 URL 打开，并在窗口关闭后回收。 */
export function openDocumentBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const opened = window.open(url, '_blank', 'noopener,noreferrer');
  if (!opened) {
    URL.revokeObjectURL(url);
    return;
  }

  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export async function addDocumentVersion(
  itemId: string,
  fileId: string,
  signal?: AbortSignal
): Promise<HostDocumentItemResponse> {
  const body: Parameters<typeof documentHostAddItemVersion>[1]['body'] = {
    fileId,
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
  return value;
}

export type { HostDocumentItemPage, HostDocumentItemResponse, HostDocumentVersionResponse };
