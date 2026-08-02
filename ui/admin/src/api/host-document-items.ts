import {
  isHostDocumentItem,
  isHostDocumentItemPage,
  type HostDocumentItem,
  type HostDocumentItemPage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostDocumentItems(
  page = 1,
  pageSize = 20
): Promise<HostDocumentItemPage> {
  const value = await request<unknown>(
    `/api/v1/document/host/items?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostDocumentItemPage(value)) {
    throw new Error('client.invalid_host_document_item_page');
  }
  return value;
}

export async function createHostDocumentItem(
  title: string,
  description: string | null
): Promise<HostDocumentItem> {
  const value = await request<unknown>('/api/v1/document/host/items', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ title, description })
  });
  if (!isHostDocumentItem(value)) {
    throw new Error('client.invalid_host_document_item');
  }
  return value;
}

export async function updateHostDocumentItem(
  itemId: string,
  title: string,
  description: string | null,
  version: number
): Promise<HostDocumentItem> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ title, description, version })
    }
  );
  if (!isHostDocumentItem(value)) {
    throw new Error('client.invalid_host_document_item');
  }
  return value;
}

export async function addHostDocumentVersion(
  itemId: string,
  fileId: string
): Promise<HostDocumentItem> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/versions`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ fileId })
    }
  );
  if (!isHostDocumentItem(value)) {
    throw new Error('client.invalid_host_document_item');
  }
  return value;
}

export async function deleteHostDocumentItem(
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

export async function restoreHostDocumentItem(
  itemId: string,
  version: number
): Promise<HostDocumentItem> {
  const value = await request<unknown>(
    `/api/v1/document/host/items/${encodeURIComponent(itemId)}/restore`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
  if (!isHostDocumentItem(value)) {
    throw new Error('client.invalid_host_document_item');
  }
  return value;
}
