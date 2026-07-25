import { describe, expect, it, vi, beforeEach } from 'vitest';
import { listAuditingExceptionLogs } from './exception-logs';

vi.mock('./http', () => ({
  request: vi.fn()
}));

import { request } from './http';

describe('exception-logs api', () => {
  beforeEach(() => {
    vi.mocked(request).mockReset();
  });

  it('lists exception logs', async () => {
    vi.mocked(request).mockResolvedValue({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        occurredAtUtc: '2026-07-25T08:00:00.000Z',
        exceptionType: 'System.InvalidOperationException',
        message: 'auditing.exception_probe',
        stackTrace: null,
        httpMethod: 'POST',
        requestPath: '/api/v1/auditing/exception-probes',
        userId: null,
        tenantId: null,
        traceId: null,
        clientIpFingerprint: null
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listAuditingExceptionLogs(1, 20);
    expect(request).toHaveBeenCalledWith(
      '/api/v1/auditing/exception-logs?page=1&pageSize=20'
    );
    expect(page.total).toBe(1);
    expect(page.items[0].message).toBe('auditing.exception_probe');
  });
});
