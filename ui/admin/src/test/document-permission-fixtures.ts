import { shareDocument, shareId, otherShareId } from './document-share-fixtures';
export const permissionDocument = { ...shareDocument, version: 7 };
export const permissionEntry = { id: shareId, documentId: permissionDocument.id, userId: shareId, permissionLevel: 'read', createdAtUtc: '2026-10-16T00:00:00Z' };
export const otherPermissionEntry = { ...permissionEntry, id: otherShareId, userId: otherShareId, permissionLevel: 'write' };
export const permissionSnapshot = (permissions = [permissionEntry, otherPermissionEntry], version = 7) => ({ documentId: permissionDocument.id, version, permissions });
export const permissionDocumentPage = (items = [permissionDocument], page = 1, pageSize = 20, total = items.length) => ({ items, page, pageSize, total });
