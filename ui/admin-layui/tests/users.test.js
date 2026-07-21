import { describe, expect, it, vi } from 'vitest';
import { createUsersController } from '../js/core/users.js';

describe('Layui Host 用户控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-users-create-form>
        <input name="username">
        <input name="displayName">
        <input name="password">
      </form>
      <div data-users-problem hidden><strong></strong><span></span></div>
      <div data-users-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'user-1',
          username: 'operator',
          displayName: '运维账号',
          isActive: true,
          createdAtUtc: '2026-07-21T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'user-2',
        username: 'new-user',
        displayName: '新用户',
        isActive: true,
        createdAtUtc: '2026-07-21T01:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'user-2',
          username: 'new-user',
          displayName: '新用户',
          isActive: true,
          createdAtUtc: '2026-07-21T01:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createUsersController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="username"]').value = 'new-user';
    document.querySelector('[name="displayName"]').value = '新用户';
    document.querySelector('[name="password"]').value = 'FullNet!2026Secure';

    await controller.load();
    expect(document.querySelector('[data-users-directory] code')?.textContent).toBe('operator');

    document.querySelector('[data-users-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/users',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          username: 'new-user',
          displayName: '新用户',
          password: 'FullNet!2026Secure'
        })
      })
    );
    controller.dispose();
  });
});
