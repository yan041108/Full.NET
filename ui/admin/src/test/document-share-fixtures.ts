import { createOutputSession, outputId } from './data-output-fixtures';
export const shareId = outputId;
export const otherShareId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
export const documentShare = { id: shareId, documentId: '01912345-6789-7abc-8def-0123456789ab', shareCode: 'SHARE-OLD', createdAtUtc: '2026-10-10T00:00:00Z', expireTime: '2026-10-17T00:00:00Z', maxAccessCount: 10, accessCount: 0, isEnabled: true, version: 1, hasPassword: false };
export function createShareSession(permissions: string[]) { const context = createOutputSession(permissions); Object.assign(context.session.currentUser!, { tenantId: null, scope: 'host', actorScope: 'host' }); return context; }
export const shareDocument = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    documentNo: 'DOC-000001',
    title: 'Spec',
    description: 'integration',
    categoryId: null,
    categoryName: null,
    categoryColor: null,
    documentType: 1,
    sizeKb: 0,
    thumbnail: null,
    status: 1,
    accessCount: 0,
    sort: 0,
    lastAccessTime: null,
    currentVersion: {
        id: '01912345-6789-7abc-8def-0123456789ac',
        versionNumber: 1,
        fileId: '01912345-6789-7abc-8def-0123456789ad',
        contentHash: 'a'.repeat(64),
        sizeBytes: 12,
        changeDescription: null,
        createdAtUtc: '2026-08-02T00:00:00Z',
        uploadedByUserId: '01912345-6789-7abc-8def-0123456789ae'
    },
    tags: [],
    createdAtUtc: '2026-08-02T00:00:00Z',
    createdByUserId: '01912345-6789-7abc-8def-0123456789af',
    updatedAtUtc: null,
    updatedByUserId: null,
    deletedAtUtc: null,
    deletedByUserId: null,
    version: 2
};
