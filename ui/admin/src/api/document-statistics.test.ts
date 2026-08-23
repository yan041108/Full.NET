import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDocumentStatistics } from './document-statistics';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('document-statistics api', () => {
  it('loads document statistics', async () => {
    requestMock.mockResolvedValueOnce({
      summary: {
        totalItems: 3,
        totalVersions: 4,
        totalSizeKb: 128,
        totalSizeInfo: '128 KB'
      },
      byType: [{ extension: '.pdf', count: 2, totalSizeKb: 96 }],
      byCategory: [{ categoryId: null, categoryName: 'Uncategorized', count: 3 }],
      shareCount: 1,
      todayAccessCount: 0,
      todayDownloadCount: 0,
      todayCreatedCount: 1,
      recycleBinCount: 0
    });

    const statistics = await getDocumentStatistics();
    expect(statistics.summary.totalItems).toBe(3);
    expect(statistics.byType).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/statistics',
      { method: 'GET' },
      undefined
    );
  });
});
