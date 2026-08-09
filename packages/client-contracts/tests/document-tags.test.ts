import { describe, expect, it } from 'vitest';
import { isHostDocumentTagResponse, isHostDocumentTagResponseList } from '../src/document-tags';

describe('document-tags contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    name: 'Release',
    code: null,
    icon: null,
    color: null,
    description: null,
    useCount: 0,
    createdAtUtc: '2026-08-02T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  it('accepts valid tag payloads', () => {
    expect(isHostDocumentTagResponse(sample)).toBe(true);
    expect(isHostDocumentTagResponseList([sample])).toBe(true);
  });

  it('rejects invalid ids', () => {
    expect(isHostDocumentTagResponse({ ...sample, id: 'bad' })).toBe(false);
  });
});
