import {
  isAddHostDocumentVersionRequest,
  isHostDocumentItemPage,
  isHostDocumentItemResponse,
  type AddHostDocumentVersionRequest,
  type CreateHostDocumentItemRequest,
  type DeleteHostDocumentItemRequest,
  type HostDocumentItemPage,
  type HostDocumentItemResponse,
  type RestoreHostDocumentItemRequest,
  type UpdateHostDocumentItemRequest
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

export async function getDocumentItem(
  id: string
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}`
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function createDocumentItem(
  req: CreateHostDocumentItemRequest
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>('/api/v1/document/host/items', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(req)
  });
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function updateDocumentItem(
  id: string,
  req: UpdateHostDocumentItemRequest
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function addDocumentVersion(
  id: string,
  req: AddHostDocumentVersionRequest
): Promise<HostDocumentItemResponse> {
  if (!isAddHostDocumentVersionRequest(req)) {
    throw new Error('client.invalid_add_document_version_request');
  }
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}/versions`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}

export async function uploadDocumentVersion(
  id: string,
  file: File
): Promise<HostDocumentItemResponse> {
  const formData = new FormData();
  formData.append('file', file);
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}/versions/upload`,
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

export async function downloadDocumentContent(id: string): Promise<Blob> {
  return requestBlob(
    `/api/v1/document/host/items/${encodeURIComponent(id)}/content`
  );
}

export function openDocumentBlob(blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const opened = window.open(url, '_blank', 'noopener,noreferrer');
  if (!opened) {
    URL.revokeObjectURL(url);
    return;
  }
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export async function deleteDocumentItem(
  id: string,
  req: DeleteHostDocumentItemRequest
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  return value === true;
}

export async function restoreDocumentItem(
  id: string,
  req: RestoreHostDocumentItemRequest
): Promise<HostDocumentItemResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(id)}/restore`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentItemResponse(value)) {
    throw new Error('client.invalid_document_item');
  }
  return value;
}
