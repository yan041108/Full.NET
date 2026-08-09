import { describe, expect, it } from 'vitest';
import {
  isHostDocumentShareResponse,
  isHostDocumentStatisticsResponse
} from '../src/index';

describe('document-shares contracts', () => {
  const sample = {
    id: '01912345-6789-7abc-8def-0123456789ab',
    documentId: '01912345-6789-7abc-8def-0123456789ac',
    shareCode: 'abc123',
    createdAtUtc: '2026-08-02T00:00:00Z',
    expireTime: '2026-09-02T00:00:00Z',
    maxAccessCount: null,
    accessCount: 0,
    isEnabled: true,
    version: 1,
    hasPassword: true
  };

  it('accepts share responses without credential fields', () => {
    expect(isHostDocumentShareResponse(sample)).toBe(true);
  });

  it('rejects password or passwordHash in share responses', () => {
    expect(isHostDocumentShareResponse({ ...sample, password: 'secret' })).toBe(false);
    expect(isHostDocumentShareResponse({ ...sample, passwordHash: 'hash' })).toBe(false);
  });
});

describe('document-statistics contracts', () => {
  const sample = {
    summary: {
      totalItems: 1,
      totalVersions: 1,
      totalSizeKb: 12,
      totalSizeInfo: '12 KB'
    },
    byType: [{ extension: '.pdf', count: 1, totalSizeKb: 12 }],
    byCategory: [{ categoryId: null, categoryName: null, count: 1 }],
    shareCount: 0,
    todayAccessCount: 0,
    todayDownloadCount: 0,
    todayCreatedCount: 0,
    recycleBinCount: 0
  };

  it('accepts nested statistics payloads', () => {
    expect(isHostDocumentStatisticsResponse(sample)).toBe(true);
  });
});
