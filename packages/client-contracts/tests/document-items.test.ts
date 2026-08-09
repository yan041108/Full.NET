import { describe, expect, it } from 'vitest';
import { isHostDocumentItemPage, isHostDocumentItemResponse } from '../src/document-items';

describe('document-items contracts', () => {
  const sample = {
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

  it('accepts valid document item payloads', () => {
    expect(isHostDocumentItemResponse(sample)).toBe(true);
    expect(isHostDocumentItemPage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('rejects invalid ids', () => {
    expect(isHostDocumentItemResponse({ ...sample, id: 'bad' })).toBe(false);
  });
});
