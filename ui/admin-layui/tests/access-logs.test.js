import { describe, expect, it, vi } from 'vitest';
import { createAccessLogsController } from '../js/core/access-logs.js';

describe('Layui 访问日志控制器', () => {
  it('加载访问日志列表', async () => {
    document.body.innerHTML = `
      <div data-access-logs-problem hidden><strong></strong><span></span></div>
      <div data-access-logs-directory></div>
      <button data-access-logs-load-more></button>
    `;
    const request = vi.fn()
      .mockResolvedValueOnce({
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
        nextCursor: 'cursor+/=',
        hasMore: true
      })
      .mockResolvedValueOnce({
        items: [{
          id: '01912345-6789-7abc-8def-0123456789ac',
          occurredAtUtc: '2026-07-25T07:59:59.000Z',
          httpMethod: 'POST',
          requestPath: '/api/v1/settings/config-entries',
          statusCode: 204,
          durationMs: 18,
          userId: null,
          tenantId: null,
          traceId: null,
          clientIpFingerprint: null,
          isAuthenticated: true
        }],
        nextCursor: null,
        hasMore: false
      });

    const controller = createAccessLogsController(document, {
      request,
      translation: () => ({
        t: key => key
      })
    });

    await controller.load();
    expect(request).toHaveBeenCalledWith(
      '/api/v1/auditing/access-logs/cursor?limit=20'
    );
    expect(document.querySelector('[data-access-logs-directory] strong')?.textContent)
      .toContain('/api/v1/settings/enum-catalogs');
    document.querySelector('[data-access-logs-load-more]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/auditing/access-logs/cursor?limit=20&cursor=cursor%2B%2F%3D'
    );
    expect(document.querySelectorAll('[data-access-logs-directory] article'))
      .toHaveLength(2);
    expect(document.querySelector('[data-access-logs-load-more]').hidden)
      .toBe(true);
    controller.dispose();
  });

  it('加载更多期间刷新时等待追加完成并重新请求首批', async () => {
    document.body.innerHTML = `
      <div data-access-logs-problem hidden><strong></strong><span></span></div>
      <div data-access-logs-directory></div>
      <button data-access-logs-load-more></button>
    `;
    let resolveLoadMore;
    const loadMoreResult = new Promise(resolve => {
      resolveLoadMore = resolve;
    });
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [createLog('01912345-6789-7abc-8def-0123456789ab', '/first')],
        nextCursor: 'next',
        hasMore: true
      })
      .mockReturnValueOnce(loadMoreResult)
      .mockResolvedValueOnce({
        items: [createLog('01912345-6789-7abc-8def-0123456789ad', '/refreshed')],
        nextCursor: null,
        hasMore: false
      });
    const controller = createAccessLogsController(document, {
      request,
      translation: () => ({ t: key => key })
    });
    await controller.load();

    document.querySelector('[data-access-logs-load-more]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    const refresh = controller.load();
    resolveLoadMore({
      items: [createLog('01912345-6789-7abc-8def-0123456789ac', '/second')],
      nextCursor: null,
      hasMore: false
    });
    await refresh;

    expect(request).toHaveBeenCalledTimes(3);
    expect(document.querySelectorAll('[data-access-logs-directory] article'))
      .toHaveLength(1);
    expect(document.querySelector('[data-access-logs-directory]').textContent)
      .toContain('/refreshed');
    controller.dispose();
  });
});

function createLog(id, requestPath) {
  return {
    id,
    occurredAtUtc: '2026-07-25T08:00:00.000Z',
    httpMethod: 'GET',
    requestPath,
    statusCode: 200,
    durationMs: 12,
    userId: null,
    tenantId: null,
    traceId: null,
    clientIpFingerprint: null,
    isAuthenticated: true
  };
}
