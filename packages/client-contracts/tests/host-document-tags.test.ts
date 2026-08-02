import { describe, expect, it } from 'vitest';
import { isHostDocumentTag, isHostDocumentTagList } from '../src/host-document-tags';

describe('host-document-tags contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    name: 'release',
    createdAtUtc: '2026-08-02T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  it('accepts valid tag payloads', () => {
    expect(isHostDocumentTag(sample)).toBe(true);
    expect(isHostDocumentTagList([sample])).toBe(true);
  });
});
