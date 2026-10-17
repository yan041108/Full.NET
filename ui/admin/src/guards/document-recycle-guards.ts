import { isGuid, isHostRecycleBinItemResponse, isHostRecycleBinPage, type HostRecycleBinItemResponse, type HostRecycleBinPage } from '@fullnet/client-contracts';
export const validRecycleId = (id: unknown): id is string => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000';
export const validRecycleRevision = (version: number): boolean => Number.isSafeInteger(version) && version > 0;
/** 软删除不改业务状态，删除审计字段才表示回收站归属。 */
export function validRecycleItem(value: unknown): value is HostRecycleBinItemResponse {
  return isHostRecycleBinItemResponse(value) && validRecycleId(value.id) && validRecycleRevision(value.version)
    && typeof value.deletedAtUtc === 'string' && !!value.deletedAtUtc.trim() && validRecycleId(value.deletedByUserId);
}
export function validRecyclePage(value: unknown, page: number, pageSize: number): value is HostRecycleBinPage {
  return isHostRecycleBinPage(value) && value.page === page && value.pageSize === pageSize && Number.isSafeInteger(value.total) && value.total >= 0
    && value.items.length <= pageSize && value.items.every(validRecycleItem) && new Set(value.items.map(row => row.id.toLowerCase())).size === value.items.length;
}
export function validRestoredRecycleItem(value: unknown, id: string, version: number): value is HostRecycleBinItemResponse {
  return isHostRecycleBinItemResponse(value) && validRecycleId(value.id) && value.id.toLowerCase() === id.toLowerCase()
    && validRecycleRevision(value.version) && value.version > version && value.deletedAtUtc === null && value.deletedByUserId === null;
}
