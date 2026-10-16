import {
  documentHostListDocumentPermissions,
  documentHostSetDocumentPermissions,
  documentHostGetDocumentPermissionSnapshot,
  type HostDocumentPermissionSnapshotResponse,
  isHostDocumentPermissionResponseList,
  isSetHostDocumentPermissionsRequest,
  type HostDocumentPermissionResponse,
  type SetHostDocumentPermissionsRequest
} from '@fullnet/client-contracts';
import { http } from './http';
import { permissionRowsMatch, trimPermissionText, validPermissionId, validPermissionRows, validPermissionSnapshot } from './document-permission-guards';

/** 查询指定文档的权限列表，并对响应结构做失败关闭校验。 */
export async function getDocumentPermissionsByDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  if (!validPermissionId(documentId)) throw new Error('client.invalid_document_permission_identity');
  const value = await documentHostListDocumentPermissions(
    http,
    { documentId },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  if (!validPermissionRows(value, documentId)) throw new Error('client.invalid_document_permission_identity');
  return value;
}

/** 整体设置文档权限；请求与响应都必须通过运行时契约校验。 */
export async function setDocumentPermissions(
  req: SetHostDocumentPermissionsRequest,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  if (!isSetHostDocumentPermissionsRequest(req) || !validPermissionId(req.documentId) || req.permissions.some(row => !validPermissionId(row.userId) || !trimPermissionText(row.permissionLevel))) {
    throw new Error('client.invalid_set_document_permissions_request');
  }
  // 同时冻结发送参数与校验依据，等待响应时调用方变更不能迁移本次操作身份。
  const payload = { documentId: req.documentId, version: req.version, permissions: req.permissions.map(entry => ({ ...entry })) };
  const value = await documentHostSetDocumentPermissions(
    http,
    { body: payload },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  if (!permissionRowsMatch(value, payload)) throw new Error('client.invalid_document_permission_identity');
  return value;
}

/** 编辑使用带修订号的一致性快照，空集合仍能参与并发检查。 */
export async function getDocumentPermissionSnapshot(documentId: string, signal?: AbortSignal): Promise<HostDocumentPermissionSnapshotResponse> {
  if (!validPermissionId(documentId)) throw new Error('client.invalid_document_permission_snapshot');
  const value = await documentHostGetDocumentPermissionSnapshot(http, { documentId }, signal);
  if (!validPermissionSnapshot(value, documentId)) {
    throw new Error('client.invalid_document_permission_snapshot');
  }
  return value;
}

/** 导出文档权限明细与写入请求模型，供权限页列表与整量保存表单共享同一契约。 */
export type { HostDocumentPermissionResponse, SetHostDocumentPermissionsRequest };
