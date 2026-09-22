import {
  documentHostBatchCreateDocumentShares,
  documentHostCreateDocumentShare,
  documentHostListDocumentShares,
  documentHostUpdateDocumentShareStatus,
  documentPublicAccessDocumentShare,
  documentPublicContentDocumentShare,
  documentPublicContentDocumentSharePreviewTask,
  documentPublicCreateDocumentSharePreviewTask,
  documentPublicGetDocumentSharePreviewTask,
  isAccessHostDocumentShareRequest,
  isCreateHostDocumentShareRequest,
  isHostDocumentShareAccessResponse,
  isHostDocumentSharePage,
  isHostDocumentShareResponse,
  isUpdateHostDocumentShareStatusRequest,
  type AccessHostDocumentShareRequest,
  type BatchCreateHostDocumentSharesRequest,
  type BatchCreateHostDocumentSharesResponse,
  type CreateHostDocumentShareRequest,
  type HostDocumentShareAccessResponse,
  type HostDocumentSharePage,
  type HostDocumentShareResponse,
  type HostDocumentPreviewTaskResponse,
  type UpdateHostDocumentShareStatusRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export type {
  BatchCreateHostDocumentShareItem,
  BatchCreateHostDocumentSharesRequest,
  BatchCreateHostDocumentSharesResponse
} from '@fullnet/client-contracts';

export interface DocumentShareListFilters {
  readonly isEnabled?: boolean;
  readonly shareCode?: string;
  readonly documentId?: string;
  readonly expiredOnly?: boolean;
  readonly activeOnly?: boolean;
  readonly minAccessCount?: number;
  readonly maxAccessCount?: number;
  readonly sortBy?: string;
  readonly sortDir?: 'asc' | 'desc';
}

/** 分页查询文档分享列表，并对响应页做失败关闭校验。 */
export async function listDocumentShares(
  page = 1,
  pageSize = 20,
  filters: DocumentShareListFilters = {},
  signal?: AbortSignal
): Promise<HostDocumentSharePage> {
  const value = await documentHostListDocumentShares(
    http,
    {
      page,
      pageSize,
      isEnabled: filters.isEnabled,
      shareCode: filters.shareCode?.trim() || undefined,
      documentId: filters.documentId?.trim() || undefined,
      expiredOnly: filters.expiredOnly || undefined,
      activeOnly: filters.activeOnly || undefined,
      minAccessCount: filters.minAccessCount,
      maxAccessCount: filters.maxAccessCount,
      sortBy: filters.sortBy,
      sortDir: filters.sortDir
    },
    signal
  );
  if (!isHostDocumentSharePage(value)) {
    throw new Error('client.invalid_document_share_page');
  }
  return value;
}

/** 创建文档分享；请求与响应都必须通过运行时契约校验。 */
export async function createDocumentShare(
  req: CreateHostDocumentShareRequest,
  signal?: AbortSignal
): Promise<HostDocumentShareResponse> {
  if (!isCreateHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_create_document_share_request');
  }
  const value = await documentHostCreateDocumentShare(http, { body: req }, signal);
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

/** 更新文档分享状态，例如启用、停用或过期。 */
export async function updateDocumentShareStatus(
  id: string,
  req: UpdateHostDocumentShareStatusRequest,
  signal?: AbortSignal
): Promise<HostDocumentShareResponse> {
  if (!isUpdateHostDocumentShareStatusRequest(req)) {
    throw new Error('client.invalid_update_document_share_status_request');
  }
  const value = await documentHostUpdateDocumentShareStatus(
    http,
    { id, body: req },
    signal
  );
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

/** 批量创建文档分享；逐文档返回成功或失败原因。 */
export async function batchCreateDocumentShares(
  req: BatchCreateHostDocumentSharesRequest,
  signal?: AbortSignal
): Promise<BatchCreateHostDocumentSharesResponse> {
  return documentHostBatchCreateDocumentShares(http, { body: req }, signal);
}

/** 通过分享码访问文档分享，可附带访问密码等公开访问参数。 */
export async function accessDocumentShareByCode(
  shareCode: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<HostDocumentShareAccessResponse> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  const value = await documentPublicAccessDocumentShare(
    http,
    { shareCode, body: req },
    signal,
    { retryUnauthorized: false, skipAuthentication: true }
  );
  if (!isHostDocumentShareAccessResponse(value)) {
    throw new Error('client.invalid_document_share_access');
  }
  return value;
}

/** 通过分享码读取文档文件内容（匿名，可附带访问密码）。 */
export async function loadDocumentShareContentByCode(
  shareCode: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<Blob> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  return documentPublicContentDocumentShare(
    http,
    { shareCode, body: req },
    signal,
    { retryUnauthorized: false, skipAuthentication: true }
  );
}

const publicShareOptions = { retryUnauthorized: false, skipAuthentication: true };

/** 匿名分享：提交 Office 预览转换任务。 */
export async function createDocumentSharePreviewTaskByCode(
  shareCode: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<HostDocumentPreviewTaskResponse> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  return documentPublicCreateDocumentSharePreviewTask(
    http,
    { shareCode, body: req },
    signal,
    publicShareOptions
  );
}

/** 匿名分享：查询预览任务状态。 */
export async function getDocumentSharePreviewTaskByCode(
  shareCode: string,
  taskId: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<HostDocumentPreviewTaskResponse> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  return documentPublicGetDocumentSharePreviewTask(
    http,
    { shareCode, taskId, body: req },
    signal,
    publicShareOptions
  );
}

/** 匿名分享：读取已完成的预览任务输出（通常为 PDF）。 */
export async function loadDocumentSharePreviewTaskContentByCode(
  shareCode: string,
  taskId: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<Blob> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  return documentPublicContentDocumentSharePreviewTask(
    http,
    { shareCode, taskId, body: req },
    signal,
    publicShareOptions
  );
}

/** 导出分享页所需的请求、分页与公开访问模型，避免管理端和公开访问流程契约漂移。 */
export type {
  AccessHostDocumentShareRequest,
  CreateHostDocumentShareRequest,
  HostDocumentShareAccessResponse,
  HostDocumentSharePage,
  HostDocumentShareResponse,
  UpdateHostDocumentShareStatusRequest
};
