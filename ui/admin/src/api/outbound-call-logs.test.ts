import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listAuditingOutboundCallLogs } from './outbound-call-logs';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('outbound-call-logs api', () => {
  it('lists outbound call logs', async () => {
    requestMock.mockResolvedValueOnce({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        occurredAtUtc: '2026-07-25T08:00:00.000Z',
        providerKey: 'smtp',
        operationKey: 'notifications.email.send',
        destinationHostCategory: 'external',
        statusCode: 200,
        succeeded: true,
        durationMs: 120,
        retryCount: 0,
        traceId: null,
        safeErrorCode: null,
        tenantId: null,
        userId: null
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listAuditingOutboundCallLogs(1, 20);
    expect(page.total).toBe(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/auditing/outbound-call-logs?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('forwards outbound filters through the generated operation', async () => {
    requestMock.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });
    await listAuditingOutboundCallLogs(1, 20, undefined, {
      providerKey: 'smtp',
      operationContains: 'send',
      succeeded: false,
      fromUtc: '2026-09-28T00:00:00Z',
      toUtc: '2026-09-29T00:00:00Z'
    });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/auditing/outbound-call-logs?page=1&pageSize=20&fromUtc=2026-09-28T00%3A00%3A00Z&toUtc=2026-09-29T00%3A00%3A00Z&providerKey=smtp&succeeded=false&operationContains=send',
      { method: 'GET' }, undefined
    );
  });
});
