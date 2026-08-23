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

});

