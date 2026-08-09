import {
  isHostDocumentCategoryResponse,
  isHostDocumentCategoryResponseList,
  type CreateHostDocumentCategoryRequest,
  type DeleteHostDocumentCategoryRequest,
  type HostDocumentCategoryResponse,
  type UpdateHostDocumentCategoryRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listDocumentCategories(): Promise<HostDocumentCategoryResponse[]> {
  const value = await request<unknown>('/api/v1/document/host/categories');
  if (!isHostDocumentCategoryResponseList(value)) {
    throw new Error('client.invalid_document_category_list');
  }
  return value;
}

export async function getDocumentCategory(
  id: string
): Promise<HostDocumentCategoryResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}`
  );
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function createDocumentCategory(
  req: CreateHostDocumentCategoryRequest
): Promise<HostDocumentCategoryResponse> {
  const value = await request<unknown>('/api/v1/document/host/categories', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(req)
  });
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function updateDocumentCategory(
  id: string,
  req: UpdateHostDocumentCategoryRequest
): Promise<HostDocumentCategoryResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function deleteDocumentCategory(
  id: string,
  req: DeleteHostDocumentCategoryRequest
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  return value === true;
}
