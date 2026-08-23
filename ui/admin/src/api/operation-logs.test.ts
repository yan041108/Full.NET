import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listAuditingOperationLogs } from './operation-logs';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('operation-logs api', () => {
  it('lists operation logs', async () => {
    requestMock.mockResolvedValueOnce({
      items: [{
        id: '01912345-6789-7abc-8def-0123456789ab',
        occurredAtUtc: '2026-07-25T08:00:00.000Z',
        actionKey: 'settings.config-entry.updated',
        httpMethod: 'PUT',
        requestPath: '/api/v1/settings/config-entries/1',
        statusCode: 200,
        durationMs: 20,
        succeeded: true,
        userId: null,
        tenantId: null,
        traceId: null,
        clientIpFingerprint: null,
        permissionCode: 'settings.config_entries.update'
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listAuditingOperationLogs(1, 20);
    expect(page.total).toBe(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/auditing/operation-logs?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });
});
