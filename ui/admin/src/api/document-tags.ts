import {
  isHostDocumentTagResponse,
  isHostDocumentTagResponseList,
  type CreateHostDocumentTagRequest,
  type DeleteHostDocumentTagRequest,
  type HostDocumentTagResponse,
  type UpdateHostDocumentTagRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listDocumentTags(): Promise<HostDocumentTagResponse[]> {
  const value = await request<unknown>('/api/v1/document/host/tags');
  if (!isHostDocumentTagResponseList(value)) {
    throw new Error('client.invalid_document_tag_list');
  }
  return value;
}

export async function getDocumentTag(
  id: string
): Promise<HostDocumentTagResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}`
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function createDocumentTag(
  req: CreateHostDocumentTagRequest
): Promise<HostDocumentTagResponse> {
  const value = await request<unknown>('/api/v1/document/host/tags', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(req)
  });
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function updateDocumentTag(
  id: string,
  req: UpdateHostDocumentTagRequest
): Promise<HostDocumentTagResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return value;
}

export async function deleteDocumentTag(
  id: string,
  req: DeleteHostDocumentTagRequest
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/tags/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  return value === true;
}
