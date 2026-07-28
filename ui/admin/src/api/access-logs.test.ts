import { describe, expect, it, vi, beforeEach } from 'vitest';
import {
  listAuditingAccessLogs,
  listAuditingAccessLogsByCursor
} from './access-logs';

vi.mock('./http', () => ({
  request: vi.fn()
}));

import { request } from './http';

describe('access-logs api', () => {
  beforeEach(() => {
    vi.mocked(request).mockReset();
  });

  it('lists access logs', async () => {
    vi.mocked(request).mockResolvedValue({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        occurredAtUtc: '2026-07-25T08:00:00.000Z',
        httpMethod: 'GET',
        requestPath: '/api/v1/settings/enum-catalogs',
        statusCode: 200,
        durationMs: 12,
        userId: null,
        tenantId: null,
        traceId: null,
        clientIpFingerprint: null,
        isAuthenticated: true
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listAuditingAccessLogs(1, 20);
    expect(request).toHaveBeenCalledWith(
      '/api/v1/auditing/access-logs?page=1&pageSize=20'
    );
    expect(page.total).toBe(1);
    expect(page.items[0].requestPath).toBe('/api/v1/settings/enum-catalogs');
  });

  it('lists and continues access logs by encoded cursor', async () => {
    vi.mocked(request)
      .mockResolvedValueOnce({
        items: [],
        nextCursor: 'cursor+/=',
        hasMore: true
      })
      .mockResolvedValueOnce({
        items: [],
        nextCursor: null,
        hasMore: false
      });

    const first = await listAuditingAccessLogsByCursor();
    await listAuditingAccessLogsByCursor(first.nextCursor);

    expect(request).toHaveBeenNthCalledWith(
      1,
      '/api/v1/auditing/access-logs/cursor?limit=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/auditing/access-logs/cursor?limit=20&cursor=cursor%2B%2F%3D'
    );
  });
});
