import {
  documentHostListDocumentPermissions,
  documentHostSetDocumentPermissions,
  isHostDocumentPermissionResponseList,
  isSetHostDocumentPermissionsRequest,
  type HostDocumentPermissionResponse,
  type SetHostDocumentPermissionsRequest
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询指定文档的权限列表，并对响应结构做失败关闭校验。 */
export async function getDocumentPermissionsByDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  const value = await documentHostListDocumentPermissions(
    http,
    { documentId },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}

/** 整体设置文档权限；请求与响应都必须通过运行时契约校验。 */
export async function setDocumentPermissions(
  req: SetHostDocumentPermissionsRequest,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  if (!isSetHostDocumentPermissionsRequest(req)) {
    throw new Error('client.invalid_set_document_permissions_request');
  }
  const value = await documentHostSetDocumentPermissions(
    http,
    { body: req },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}

/** 导出文档权限明细与写入请求模型，供权限页列表与整量保存表单共享同一契约。 */
export type { HostDocumentPermissionResponse, SetHostDocumentPermissionsRequest };
