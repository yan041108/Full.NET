import { describe, expect, it, vi } from 'vitest';
import { createMenusController } from '../js/core/menus.js';

describe('Layui Host 菜单控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-menus-create-form>
        <input name="routeName">
        <select name="componentKey"><option value="overview">overview</option></select>
        <input name="path" value="/">
        <input name="title">
        <select name="requiredPermission"><option value="platform.dashboard.read">platform.dashboard.read</option></select>
      </form>
      <div data-menus-problem hidden><strong></strong><span></span></div>
      <div data-menus-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'menu-1',
          parentId: null,
          routeName: 'custom-overview',
          path: '/',
          componentKey: 'overview',
          title: '自定义工作台',
          caption: 'Custom',
          icon: 'grid',
          displayOrder: 50,
          requiredPermission: 'platform.dashboard.read',
          isSystem: false,
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
        id: 'menu-2',
        parentId: null,
        routeName: 'parity-menu',
        path: '/',
        componentKey: 'overview',
        title: '对等菜单',
        caption: '对等菜单',
        icon: 'grid',
        displayOrder: 50,
        requiredPermission: 'platform.dashboard.read',
        isSystem: false,
        isActive: true,
        createdAtUtc: '2026-07-21T01:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'menu-2',
          parentId: null,
          routeName: 'parity-menu',
          path: '/',
          componentKey: 'overview',
          title: '对等菜单',
          caption: '对等菜单',
          icon: 'grid',
          displayOrder: 50,
          requiredPermission: 'platform.dashboard.read',
          isSystem: false,
          isActive: true,
          createdAtUtc: '2026-07-21T01:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createMenusController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="routeName"]').value = 'parity-menu';
    document.querySelector('[name="title"]').value = '对等菜单';

    await controller.load();
    expect(document.querySelector('[data-menus-directory] code')?.textContent)
      .toBe('custom-overview · overview');

    document.querySelector('[data-menus-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/identity/menus',
      expect.objectContaining({
        method: 'POST',
        body: expect.stringContaining('parity-menu')
      })
    );
    controller.dispose();
  });
});
