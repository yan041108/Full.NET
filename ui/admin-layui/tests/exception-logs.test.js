import { describe, expect, it, vi } from 'vitest';
import { createExceptionLogsController } from '../js/core/exception-logs.js';

describe('Layui 异常日志控制器', () => {
  it('加载异常日志列表', async () => {
    document.body.innerHTML = `
      <div data-exception-logs-problem hidden><strong></strong><span></span></div>
      <div data-exception-logs-directory></div>
    `;
    const request = vi.fn().mockResolvedValueOnce({
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

    const controller = createExceptionLogsController(document, {
      request,
      translation: () => ({
        t: key => key
      })
    });

    await controller.load();
    expect(request).toHaveBeenCalledWith('/api/v1/auditing/exception-logs?page=1&pageSize=20');
    expect(document.querySelector('[data-exception-logs-directory] strong')?.textContent)
      .toContain('InvalidOperationException');
    controller.dispose();
  });
});
