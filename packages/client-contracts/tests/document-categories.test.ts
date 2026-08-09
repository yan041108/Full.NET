import { describe, expect, it } from 'vitest';
import {
  isHostDocumentCategoryResponse,
  isHostDocumentCategoryResponseList
} from '../src/document-categories';

describe('document-categories contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    parentId: null,
    name: 'Guides',
    code: null,
    sortOrder: 0,
    icon: null,
    color: null,
    description: null,
    createdAtUtc: '2026-08-02T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  it('accepts valid category payloads', () => {
    expect(isHostDocumentCategoryResponse(sample)).toBe(true);
    expect(isHostDocumentCategoryResponseList([sample])).toBe(true);
  });

  it('rejects invalid ids', () => {
    expect(isHostDocumentCategoryResponse({ ...sample, id: 'bad' })).toBe(false);
  });
});
