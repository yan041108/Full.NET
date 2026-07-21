import { describe, expect, it, vi } from 'vitest';
import { createOrgUnitsController } from '../js/core/org-units.js';

describe('Layui 租户机构控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-org-units-create-form>
        <input name="code">
        <input name="name">
      </form>
      <div data-org-units-problem hidden><strong></strong><span></span></div>
      <div data-org-units-directory></div>`;
    const request = vi.fn()
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
        id: 'unit-2',
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-21T01:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'unit-2',
          parentId: null,
          code: 'parity-unit',
          name: '对等机构',
          displayOrder: 10,
          isActive: true,
          createdAtUtc: '2026-07-21T01:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createOrgUnitsController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="code"]').value = 'parity-unit';
    document.querySelector('[name="name"]').value = '对等机构';

    await controller.load();
    expect(document.querySelector('[data-org-units-directory] code')?.textContent)
      .toBe('hq');

    document.querySelector('[data-org-units-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/organization/units',
      expect.objectContaining({
        method: 'POST',
        body: expect.stringContaining('parity-unit')
      })
    );
    controller.dispose();
  });
});
