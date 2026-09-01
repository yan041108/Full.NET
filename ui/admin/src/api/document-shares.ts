import {
  documentHostCreateDocumentShare,
  documentHostListDocumentShares,
  documentHostUpdateDocumentShareStatus,
  documentPublicAccessDocumentShare,
  isAccessHostDocumentShareRequest,
  isCreateHostDocumentShareRequest,
  isHostDocumentShareAccessResponse,
  isHostDocumentSharePage,
  isHostDocumentShareResponse,
  isUpdateHostDocumentShareStatusRequest,
  type AccessHostDocumentShareRequest,
  type CreateHostDocumentShareRequest,
  type HostDocumentShareAccessResponse,
  type HostDocumentSharePage,
  type HostDocumentShareResponse,
  type UpdateHostDocumentShareStatusRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 分页查询文档分享列表，并对响应页做失败关闭校验。 */
export async function listDocumentShares(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostDocumentSharePage> {
  const value = await documentHostListDocumentShares(
    http,
    { page, pageSize },
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
    signal
  );
  if (!isHostDocumentShareAccessResponse(value)) {
    throw new Error('client.invalid_document_share_access');
  }
  return value;
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
