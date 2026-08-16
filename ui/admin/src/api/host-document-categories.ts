import {
  isHostDocumentCategoryResponse,
  isHostDocumentCategoryResponseList,
  type CreateHostDocumentCategoryRequest,
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

export async function createDocumentCategory(
  name: string,
  parentId: string | null,
  sortOrder: number,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null
): Promise<HostDocumentCategoryResponse> {
  const value = await request<unknown>('/api/v1/document/host/categories', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name, parentId, sortOrder, code, icon, color, description })
  });
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function updateDocumentCategory(
  id: string,
  name: string,
  parentId: string | null,
  sortOrder: number,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number
): Promise<HostDocumentCategoryResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, parentId, sortOrder, code, icon, color, description, version })
    }
  );
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function deleteDocumentCategory(
  id: string,
  version: number
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
  return value === true;
}

export type {
  CreateHostDocumentCategoryRequest,
  HostDocumentCategoryResponse,
  UpdateHostDocumentCategoryRequest
};
