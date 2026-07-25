import { describe, expect, it, vi } from 'vitest';
import { createAccessLogsController } from '../js/core/access-logs.js';

describe('Layui 访问日志控制器', () => {
  it('加载访问日志列表', async () => {
    document.body.innerHTML = `
      <div data-access-logs-problem hidden><strong></strong><span></span></div>
      <div data-access-logs-directory></div>
    `;
    const request = vi.fn().mockResolvedValueOnce({
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

    const controller = createAccessLogsController(document, {
      request,
      translation: () => ({
        t: key => key
      })
    });

    await controller.load();
    expect(request).toHaveBeenCalledWith('/api/v1/auditing/access-logs?page=1&pageSize=20');
    expect(document.querySelector('[data-access-logs-directory] strong')?.textContent)
      .toContain('/api/v1/settings/enum-catalogs');
    controller.dispose();
  });
});
