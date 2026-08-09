// 为避免 barrel 层重复标识符冲突，此处用新版 Response 类型别名旧名
import {
  isHostDocumentCategoryResponse as isHostDocumentCategory,
  isHostDocumentCategoryResponseList as isHostDocumentCategoryList,
  type HostDocumentCategoryResponse as HostDocumentCategory,
  type CreateHostDocumentCategoryRequest,
  type UpdateHostDocumentCategoryRequest,
  type DeleteHostDocumentCategoryRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostDocumentCategories(): Promise<HostDocumentCategory[]> {
  const value = await request<unknown>('/api/v1/document/host/categories');
  if (!isHostDocumentCategoryList(value)) {
    throw new Error('client.invalid_host_document_category_list');
  }
  return value;
}

export async function createHostDocumentCategory(
  name: string,
  parentId: string | null,
  sortOrder: number,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null
): Promise<HostDocumentCategory> {
  const value = await request<unknown>('/api/v1/document/host/categories', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name, parentId, sortOrder, code, icon, color, description })
  });
  if (!isHostDocumentCategory(value)) {
    throw new Error('client.invalid_host_document_category');
  }
  return value;
}

export async function updateHostDocumentCategory(
  id: string,
  name: string,
  parentId: string | null,
  sortOrder: number,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number
): Promise<HostDocumentCategory> {
  const value = await request<unknown>(
    `/api/v1/document/host/categories/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, parentId, sortOrder, code, icon, color, description, version })
    }
  );
  if (!isHostDocumentCategory(value)) {
    throw new Error('client.invalid_host_document_category');
  }
  return value;
}

export async function deleteHostDocumentCategory(
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
