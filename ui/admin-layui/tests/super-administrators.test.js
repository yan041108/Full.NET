import { describe, expect, it, vi } from 'vitest';
import { createSuperAdministratorController } from '../js/core/super-administrators.js';

describe('Layui 超级管理员控制器', () => {
  it('加载安全台账并提交一次性重认证密码', async () => {
    document.body.innerHTML = `
      <form data-super-admin-grant-form>
        <input name="username">
        <input name="currentPassword">
      </form>
      <div data-super-admin-problem hidden><strong></strong><span></span></div>
      <div data-super-admin-directory></div>
      <ol data-super-admin-audits></ol>`;
    const request = vi.fn()
      .mockResolvedValueOnce([{ userId: 'u1', username: 'admin', displayName: '管理员', isActive: true }])
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce({ targetUserId: 'u2', changed: true })
      .mockResolvedValueOnce([{ userId: 'u1', username: 'admin', displayName: '管理员', isActive: true }])
      .mockResolvedValueOnce([]);
    const controller = createSuperAdministratorController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="username"]').value = 'target-admin';
    document.querySelector('[name="currentPassword"]').value = 'secret';

    await controller.load();
    expect(document.querySelector('[data-super-admin-directory] article code')?.textContent).toBe('admin');

    document.querySelector('[data-super-admin-grant-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(5));
    expect(request).toHaveBeenNthCalledWith(
      3,
      '/api/v1/identity/super-administrators/grant',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ username: 'target-admin', currentPassword: 'secret' })
      })
    );
    expect(document.querySelector('[name="currentPassword"]').value).toBe('');
    controller.dispose();
  });
});
