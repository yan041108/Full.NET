import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getAuditingOperationLogDetails, listAuditingOperationLogs } from './operation-logs';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('operation-logs api', () => {
  it('loads restricted details through the generated operation', async () => {
    const id = '01912345-6789-7abc-8def-0123456789ab';
    requestMock.mockResolvedValueOnce({
      id,
      detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: {
        schemaVersion: 1,
        clientIp: null,
        clientPort: null,
        serverIp: null,
        serverPort: null,
        requestCaptureState: 'not_applicable',
        requestSummary: null,
        responseCaptureState: 'not_applicable',
        responseSummary: null
      }
    });

    const details = await getAuditingOperationLogDetails(id);
    expect(details.id).toBe(id);
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/auditing/operation-logs/${id}/details`,
      { method: 'GET' },
      undefined
    );
  });

  it('rejects restricted details whose id differs from the requested log', async () => {
    requestMock.mockResolvedValueOnce({
      id: '01912345-6789-7abc-8def-0123456789ac',
      detailsExpiresAtUtc: '2026-09-30T00:00:00Z',
      context: { schemaVersion: 1, clientIp: '203.0.113.42', clientPort: null,
        serverIp: null, serverPort: null, requestCaptureState: 'not_applicable',
        requestSummary: null, responseCaptureState: 'not_applicable', responseSummary: null }
    });

    await expect(getAuditingOperationLogDetails('01912345-6789-7abc-8def-0123456789ab'))
      .rejects.toThrow('client.invalid_auditing_operation_log_details_id');
  });

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

  it('forwards operation log filters through the generated query operation', async () => {
    requestMock.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });

    await listAuditingOperationLogs(1, 20, undefined, {
      httpMethod: 'POST',
      succeeded: false,
      pathContains: '/api/orders'
    });

    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/auditing/operation-logs?page=1&pageSize=20&httpMethod=POST&succeeded=false&pathContains=%2Fapi%2Forders',
      { method: 'GET' },
      undefined
    );
  });
});
