import {
  documentHostCreateCategory,
  documentHostDeleteCategory,
  documentHostListCategories,
  documentHostUpdateCategory,
  isHostDocumentCategoryResponse,
  isHostDocumentCategoryResponseList,
  type CreateHostDocumentCategoryRequest,
  type HostDocumentCategoryResponse,
  type UpdateHostDocumentCategoryRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listDocumentCategories(
  signal?: AbortSignal
): Promise<HostDocumentCategoryResponse[]> {
  const value = await documentHostListCategories(http, {}, signal);
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
  description: string | null,
  signal?: AbortSignal
): Promise<HostDocumentCategoryResponse> {
  const value = await documentHostCreateCategory(
    http,
    {
      body: { name, parentId, sortOrder, code, icon, color, description }
    },
    signal
  );
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
  version: number,
  signal?: AbortSignal
): Promise<HostDocumentCategoryResponse> {
  const value = await documentHostUpdateCategory(
    http,
    {
      categoryId: id,
      body: { name, parentId, sortOrder, code, icon, color, description, version }
    },
    signal
  );
  if (!isHostDocumentCategoryResponse(value)) {
    throw new Error('client.invalid_document_category');
  }
  return value;
}

export async function deleteDocumentCategory(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostDeleteCategory(
    http,
    {
      categoryId: id,
      body: { version }
    },
    signal
  );
}

export type {
  CreateHostDocumentCategoryRequest,
  HostDocumentCategoryResponse,
  UpdateHostDocumentCategoryRequest
};
