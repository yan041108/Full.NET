import { isGuid, isHostDocumentItemPage, isHostDocumentPermissionResponseList, isHostDocumentPermissionSnapshotResponse, type HostDocumentItemPage, type HostDocumentPermissionResponse, type HostDocumentPermissionSnapshotResponse, type SetHostDocumentPermissionsRequest } from '@fullnet/client-contracts';
export const validPermissionId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
// 与服务端 Trim/IsNullOrWhiteSpace 对齐，保留 BOM，并包含 JavaScript trim 不识别的 NEL。
export const trimPermissionText = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
export function validPermissionRows(value: unknown, documentId: string): value is HostDocumentPermissionResponse[] {
  return isHostDocumentPermissionResponseList(value) && value.every(row => validPermissionId(row.id) && validPermissionId(row.userId) && row.documentId.toLowerCase() === documentId.toLowerCase() && !!trimPermissionText(row.permissionLevel))
    && new Set(value.map(row => row.id.toLowerCase())).size === value.length;
}
export function validPermissionSnapshot(value: unknown, documentId: string): value is HostDocumentPermissionSnapshotResponse {
  return validPermissionId(documentId) && isHostDocumentPermissionSnapshotResponse(value) && value.documentId.toLowerCase() === documentId.toLowerCase() && validPermissionRows(value.permissions, documentId);
}
/** 数据库允许同一用户有多条配置，核对多重集合而不擅自增加唯一用户规则。 */
export function permissionRowsMatch(value: unknown, payload: SetHostDocumentPermissionsRequest): value is HostDocumentPermissionResponse[] {
  if (!validPermissionRows(value, payload.documentId) || value.length !== payload.permissions.length) return false;
  const key = (row: { userId: string; permissionLevel: string }) => JSON.stringify([row.userId.toLowerCase(), trimPermissionText(row.permissionLevel)]), counts = new Map<string, number>();
  for (const row of payload.permissions) { const identity = key(row); counts.set(identity, (counts.get(identity) ?? 0) + 1); }
  for (const row of value) { const identity = key(row), count = counts.get(identity) ?? 0; if (count === 0) return false; counts.set(identity, count - 1); }
  return true;
}
export function permissionSignature(rows: readonly HostDocumentPermissionResponse[]): string {
  return JSON.stringify(rows.map(row => [row.id.toLowerCase(), row.documentId.toLowerCase(), row.userId.toLowerCase(), row.permissionLevel, row.createdAtUtc]).sort((left, right) => left[0]!.localeCompare(right[0]!)));
}
export function validPermissionDocumentPage(value: unknown, page: number, pageSize: number): value is HostDocumentItemPage {
  return isHostDocumentItemPage(value) && value.page === page && value.pageSize === pageSize && Number.isSafeInteger(value.total) && value.total >= 0 && value.items.length <= pageSize
    && value.items.every(row => validPermissionId(row.id) && Number.isSafeInteger(row.version) && row.version > 0 && row.deletedAtUtc === null && row.deletedByUserId === null)
    && new Set(value.items.map(row => row.id.toLowerCase())).size === value.items.length;
}
