import type { HostDocumentPermissionResponse, HostDocumentPermissionSnapshotResponse } from '@fullnet/client-contracts';
import { shareDocument, shareId, otherShareId } from './document-share-fixtures';
export const permissionDocument = { ...shareDocument, version: 7 };
export const permissionEntry: HostDocumentPermissionResponse = { id: shareId, documentId: permissionDocument.id, userId: shareId, actions: ['read'], createdAtUtc: '2026-10-16T00:00:00Z' };
export const otherPermissionEntry: HostDocumentPermissionResponse = { ...permissionEntry, id: otherShareId, userId: otherShareId, actions: ['write'] };
export const permissionSnapshot = (permissions = [permissionEntry, otherPermissionEntry], version = 7): HostDocumentPermissionSnapshotResponse => ({ documentId: permissionDocument.id, version, visibilityMode: 'restricted', permissions });
export const permissionDocumentPage = (items = [permissionDocument], page = 1, pageSize = 20, total = items.length) => ({ items, page, pageSize, total });
