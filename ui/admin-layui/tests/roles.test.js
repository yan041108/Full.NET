import { describe, expect, it, vi } from 'vitest';
import { createRolesController } from '../js/core/roles.js';

describe('Layui Host 角色控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-roles-create-form>
        <input name="code">
        <input name="name">
      </form>
      <div data-roles-problem hidden><strong></strong><span></span></div>
      <div data-roles-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'role-1',
          code: 'support',
          name: '支持角色',
          isSystem: false,
          isActive: true,
          isSuperAdministrator: false,
          permissionCodes: [],
          createdAtUtc: '2026-07-21T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'role-2',
        code: 'ops',
        name: '运维角色',
        isSystem: false,
        isActive: true,
        isSuperAdministrator: false,
        permissionCodes: [],
        createdAtUtc: '2026-07-21T01:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'role-2',
          code: 'ops',
          name: '运维角色',
          isSystem: false,
          isActive: true,
          isSuperAdministrator: false,
          permissionCodes: [],
          createdAtUtc: '2026-07-21T01:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createRolesController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="code"]').value = 'ops';
    document.querySelector('[name="name"]').value = '运维角色';

    await controller.load();
    expect(document.querySelector('[data-roles-directory] code')?.textContent).toBe('support');

    document.querySelector('[data-roles-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/roles',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ code: 'ops', name: '运维角色' })
      })
    );
    controller.dispose();
  });
});
