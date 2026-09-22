import {
  documentHostCreateTag,
  documentHostDeleteTag,
  documentHostListTags,
  documentHostUpdateTag,
  isHostDocumentTagResponse,
  isHostDocumentTagResponseList,
  normalizeHostDocumentTagResponse,
  type HostDocumentTagResponse
} from '@fullnet/client-contracts';
import { http } from './http';

export interface ListDocumentTagsOptions {
  readonly isHot?: boolean;
  readonly isRecommended?: boolean;
}

/** 查询文档标签列表，并对响应结构做失败关闭校验。 */
export async function listDocumentTags(
  options: ListDocumentTagsOptions = {},
  signal?: AbortSignal
): Promise<HostDocumentTagResponse[]> {
  const value = await documentHostListTags(
    http,
    {
      isHot: options.isHot,
      isRecommended: options.isRecommended
    },
    signal
  );
  if (!isHostDocumentTagResponseList(value)) {
    throw new Error('client.invalid_document_tag_list');
  }
  return value.map(normalizeHostDocumentTagResponse);
}

/** 创建文档标签。 */
export async function createDocumentTag(
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  isHot = false,
  isRecommended = false,
  signal?: AbortSignal
): Promise<HostDocumentTagResponse> {
  const value = await documentHostCreateTag(
    http,
    { body: { name, code, icon, color, description, isHot, isRecommended } },
    signal
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return normalizeHostDocumentTagResponse(value);
}

/** 更新文档标签，并携带版本号维持乐观并发。 */
export async function updateDocumentTag(
  id: string,
  name: string,
  code: string | null,
  icon: string | null,
  color: string | null,
  description: string | null,
  version: number,
  isHot = false,
  isRecommended = false,
  signal?: AbortSignal
): Promise<HostDocumentTagResponse> {
  const value = await documentHostUpdateTag(
    http,
    {
      tagId: id,
      body: { name, code, icon, color, description, version, isHot, isRecommended }
    },
    signal
  );
  if (!isHostDocumentTagResponse(value)) {
    throw new Error('client.invalid_document_tag');
  }
  return normalizeHostDocumentTagResponse(value);
}

/** 删除文档标签。 */
export async function deleteDocumentTag(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostDeleteTag(
    http,
    {
      tagId: id,
      body: { version }
    },
    signal
  );
}

/** 导出文档标签模型，供标签列表、编辑弹窗与选择器复用同一契约。 */
export type { HostDocumentTagResponse };
