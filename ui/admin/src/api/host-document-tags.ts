import {
  isHostDocumentTag,
  isHostDocumentTagList,
  type HostDocumentTag
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostDocumentTags(): Promise<HostDocumentTag[]> {
  const value = await request<unknown>('/api/v1/document/host/tags');
  if (!isHostDocumentTagList(value)) {
    throw new Error('client.invalid_host_document_tag_list');
  }
  return value;
}

export async function createHostDocumentTag(
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null
): Promise<HostDocumentTag> {
  const value = await request<unknown>('/api/v1/document/host/tags', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name, code, icon, color, description })
  });
  if (!isHostDocumentTag(value)) {
    throw new Error('client.invalid_host_document_tag');
  }
  return value;
}

export async function updateHostDocumentTag(
  id: string,
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number
): Promise<HostDocumentTag> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, code, icon, color, description, version })
    }
  );
  if (!isHostDocumentTag(value)) {
    throw new Error('client.invalid_host_document_tag');
  }
  return value;
}

export async function deleteHostDocumentTag(
  id: string,
  version: number
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
  return value === true;
}
