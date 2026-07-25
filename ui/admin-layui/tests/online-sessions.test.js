import { describe, expect, it, vi } from 'vitest';
import { createOnlineSessionsController } from '../js/core/online-sessions.js';

describe('Layui Host 在线会话控制器', () => {
  it('加载目录并强制下线会话', async () => {
    document.body.innerHTML = `
      <div data-online-sessions-problem hidden><strong></strong><span></span></div>
      <div data-online-sessions-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'session-1',
          userId: 'user-1',
          username: 'operator',
          displayName: '运维账号',
          clientId: 'fullnet-admin',
          activeTenantId: null,
          createdAtUtc: '2026-07-26T00:00:00Z',
          expiresAtUtc: '2026-08-26T00:00:00Z'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'session-1',
        userId: 'user-1',
        username: 'operator',
        displayName: '运维账号',
        clientId: 'fullnet-admin',
        activeTenantId: null,
        createdAtUtc: '2026-07-26T00:00:00Z',
        expiresAtUtc: '2026-08-26T00:00:00Z'
      })
      .mockResolvedValueOnce({
        items: [],
        page: 1,
        pageSize: 20,
        total: 0
      });
    globalThis.layui = {
      layer: {
        confirm: (_message, _options, callback) => callback(1),
        close: () => undefined,
        msg: () => undefined
      }
    };
    const controller = createOnlineSessionsController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });

    await controller.load();
    expect(document.querySelector('[data-online-sessions-directory] code')?.textContent)
      .toBe('operator');

    document.querySelector('[data-online-sessions-revoke]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/online-sessions/session-1/revoke',
      expect.objectContaining({ method: 'POST' })
    );
    controller.dispose();
  });
});
