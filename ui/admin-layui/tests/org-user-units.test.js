import { describe, expect, it, vi } from 'vitest';
import { createOrgUserUnitsController } from '../js/core/org-user-units.js';

describe('Layui 租户用户-机构隶属控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-org-user-units-create-form>
        <select name="userId" data-org-user-units-user></select>
        <select name="unitId" data-org-user-units-unit></select>
      </form>
      <div data-org-user-units-problem hidden><strong></strong><span></span></div>
      <div data-org-user-units-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 })
      .mockResolvedValueOnce({
        items: [{
          id: 'unit-1',
          parentId: null,
          code: 'hq',
          name: '总部',
          displayOrder: 10,
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
        items: [{
          id: 'user-1',
          username: 'admin',
          displayName: '系统管理员',
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
        id: 'assignment-1',
        userId: 'user-1',
        username: 'admin',
        displayName: '系统管理员',
        unitId: 'unit-1',
        unitCode: 'hq',
        unitName: '总部',
        isPrimary: false,
        isActive: true,
        createdAtUtc: '2026-07-21T01:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
      .mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 })
      .mockResolvedValueOnce({
        items: [{
          id: 'unit-1',
          parentId: null,
          code: 'hq',
          name: '总部',
          displayOrder: 10,
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
        items: [{
          id: 'user-1',
          username: 'admin',
          displayName: '系统管理员',
          isActive: true,
          createdAtUtc: '2026-07-21T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createOrgUserUnitsController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });

    await controller.load();
    document.querySelector('[name="userId"]').value = 'user-1';
    document.querySelector('[name="unitId"]').value = 'unit-1';
    document.querySelector('[data-org-user-units-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(7));
    expect(request).toHaveBeenNthCalledWith(
      1,
      '/api/v1/organization/user-units?page=1&pageSize=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/units?page=1&pageSize=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      3,
      '/api/v1/identity/users?page=1&pageSize=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      4,
      '/api/v1/organization/user-units',
      expect.objectContaining({
        method: 'POST',
        body: expect.stringContaining('user-1')
      })
    );
    expect(request).toHaveBeenNthCalledWith(
      5,
      '/api/v1/organization/user-units?page=1&pageSize=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      6,
      '/api/v1/organization/units?page=1&pageSize=20'
    );
    expect(request).toHaveBeenNthCalledWith(
      7,
      '/api/v1/identity/users?page=1&pageSize=20'
    );
    controller.dispose();
  });
});
