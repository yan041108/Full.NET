import { describe, expect, it } from 'vitest';
import { isHostDocumentCategory, isHostDocumentCategoryList } from '../src/host-document-categories';

describe('host-document-categories contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    parentId: null,
    name: 'General',
    sortOrder: 10,
    createdAtUtc: '2026-08-02T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  it('accepts valid category payloads', () => {
    expect(isHostDocumentCategory(sample)).toBe(true);
    expect(isHostDocumentCategoryList([sample])).toBe(true);
  });
});
