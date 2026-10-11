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
  if (value.some(entry => entry.documentId.toLowerCase() !== documentId.toLowerCase())) throw new Error('client.invalid_document_permission_identity');
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
  // 同时冻结发送参数与校验依据，等待响应时调用方变更不能迁移本次操作身份。
  const payload = { documentId: req.documentId, permissions: req.permissions.map(entry => ({ ...entry })) };
  const value = await documentHostSetDocumentPermissions(
    http,
    { body: payload },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  // 服务端裁剪 Unicode White_Space（含 NEL，不含 BOM）；结果允许乱序但不能缺项、额外或错配。
  const expected = new Map<string, number>();
  const key = (userId: string, level: string) => JSON.stringify([userId.toLowerCase(), level.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '')]);
  for (const entry of payload.permissions) {
    const identity = key(entry.userId, entry.permissionLevel); expected.set(identity, (expected.get(identity) ?? 0) + 1);
  }
  if (value.length !== payload.permissions.length) throw new Error('client.invalid_document_permission_identity');
  for (const entry of value) {
    const identity = key(entry.userId, entry.permissionLevel), count = expected.get(identity) ?? 0;
    if (entry.documentId.toLowerCase() !== payload.documentId.toLowerCase() || count === 0) throw new Error('client.invalid_document_permission_identity');
    expected.set(identity, count - 1);
  }
  return value;
}

/** 导出文档权限明细与写入请求模型，供权限页列表与整量保存表单共享同一契约。 */
export type { HostDocumentPermissionResponse, SetHostDocumentPermissionsRequest };
