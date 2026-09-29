import { describe, expect, it, vi } from 'vitest';

import { http } from './http';

import { listAuditingExceptionLogs } from './exception-logs';



vi.mock('./http', () => ({

  http: {

    request: vi.fn(),

    requestBlob: vi.fn()

  }

}));

const requestMock = vi.mocked(http.request);



describe('exception-logs api', () => {

  it('lists exception logs', async () => {

    requestMock.mockResolvedValueOnce({

      items: [{

        id: '01912345-6789-7abc-8def-0123456789ab',

        occurredAtUtc: '2026-07-25T08:00:00.000Z',

        exceptionType: 'System.InvalidOperationException',

        message: 'boom',

        stackTrace: null,

        httpMethod: 'GET',

        requestPath: '/api/v1/settings/enum-catalogs',

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

    expect(page.total).toBe(1);

    expect(requestMock).toHaveBeenCalledWith(

      '/api/v1/auditing/exception-logs?page=1&pageSize=20',

      { method: 'GET' },

      undefined

    );

  });

  it('forwards exception contains filters through the generated operation', async () => {
    requestMock.mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });
    await listAuditingExceptionLogs(1, 20, undefined, {
      exceptionTypeContains: 'InvalidOperation',
      pathContains: '/api/orders',
      fromUtc: '2026-09-28T00:00:00Z',
      toUtc: '2026-09-29T00:00:00Z'
    });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/auditing/exception-logs?page=1&pageSize=20&fromUtc=2026-09-28T00%3A00%3A00Z&toUtc=2026-09-29T00%3A00%3A00Z&exceptionTypeContains=InvalidOperation&pathContains=%2Fapi%2Forders',
      { method: 'GET' }, undefined
    );
  });

});

