import {
  isHostDocumentTagResponse,
  isHostDocumentTagResponseList,
  type HostDocumentTagResponse
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listDocumentTags(): Promise<HostDocumentTagResponse[]> {
  const value = await request<unknown>('/api/v1/document/host/tags');
  if (!isHostDocumentTagResponseList(value)) {
    throw new Error('client.invalid_document_tag_list');
  }
  return value;
}

export async function createDocumentTag(
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null
): Promise<HostDocumentTagResponse> {
  const value = await request<unknown>('/api/v1/document/host/tags', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name, code, icon, color, description })
  });
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function updateDocumentTag(
  id: string,
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number
): Promise<HostDocumentTagResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, code, icon, color, description, version })
    }
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function deleteDocumentTag(
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

export type { HostDocumentTagResponse };
