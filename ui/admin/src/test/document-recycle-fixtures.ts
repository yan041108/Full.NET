import type { HostRecycleBinItemResponse } from '@fullnet/client-contracts';
import { shareDocument } from './document-share-fixtures';
export const recycleItem: HostRecycleBinItemResponse = { ...shareDocument, deletedAtUtc: '2026-10-16T01:00:00Z', deletedByUserId: shareDocument.createdByUserId };
export const restoredRecycleItem = (row = recycleItem): HostRecycleBinItemResponse => ({ ...row, version: row.version + 1, deletedAtUtc: null, deletedByUserId: null });
export const recyclePage = (items = [recycleItem], page = 1, pageSize = 20, total = items.length) => ({ items, page, pageSize, total });
