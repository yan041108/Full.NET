import {
  isHostDocumentItemPage,
  isHostDocumentItemResponse,
  isHostDocumentVersionList,
  type HostDocumentItemPage,
  type HostDocumentItemResponse,
  type HostDocumentVersionResponse
} from '@fullnet/client-contracts';
import { request, requestBlob } from './http';

export async function listDocumentItems(
  page = 1,
  pageSize = 20
): Promise<HostDocumentItemPage> {
  const value = await request<unknown>(
    `/api/v1/document/host/items?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostDocumentItemPage(value)) {
    throw new Error('client.invalid_document_item_page');
  }
  return value;
}

export async function createDocumentItem(
  title: string,
  description: string | null
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>('/api/v1/document/host/items', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ title, description })
  });
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function updateDocumentItem(
  itemId: string,
  title: string,
  description: string | null,
  version: number
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ title, description, version })
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function uploadDocumentVersion(
  itemId: string,
  file: File
): Promise<HostDocumentItemResponse> {
  const formData = new FormData();
  formData.append('file', file);
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/versions/upload`,
    {
      method: 'POST',
      body: formData
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function downloadDocumentContent(itemId: string): Promise<Blob> {
  return requestBlob(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/content`
  );
}

export async function listDocumentVersions(
  itemId: string
): Promise<HostDocumentVersionResponse[]> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/versions`
  );
  if (!isHostDocumentVersionList(value)) {
    throw new Error('client.invalid_document_version_list');
  }
  return value;
}

export async function previewDocumentContent(
  itemId: string,
  versionId?: string
): Promise<Blob> {
  const suffix = versionId
    ? `/versions/${encodeURIComponent(versionId)}/preview`
    : '/preview';
  return requestBlob(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}${suffix}`
  );
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
  fileId: string
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/versions`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ fileId })
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function deleteDocumentItem(
  itemId: string,
  version: number
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
  return value === true;
}

export async function restoreDocumentItem(
  itemId: string,
  version: number
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/restore`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export type { HostDocumentItemPage, HostDocumentItemResponse, HostDocumentVersionResponse };
