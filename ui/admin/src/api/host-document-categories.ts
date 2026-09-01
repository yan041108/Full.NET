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

/** 查询文档分类树的扁平列表，并对响应结构做失败关闭校验。 */
export async function listDocumentCategories(
  signal?: AbortSignal
): Promise<HostDocumentCategoryResponse[]> {
  const value = await documentHostListCategories(http, {}, signal);
  if (!isHostDocumentCategoryResponseList(value)) {
    throw new Error('client.invalid_document_category_list');
  }
  return value;
}

/** 创建文档分类。 */
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

/** 更新文档分类，并携带版本号维持乐观并发。 */
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

/** 删除文档分类。 */
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

/** 导出文档分类读写模型，供分类树、编辑弹窗与保存请求共享同一契约。 */
export type {
  CreateHostDocumentCategoryRequest,
  HostDocumentCategoryResponse,
  UpdateHostDocumentCategoryRequest
};
