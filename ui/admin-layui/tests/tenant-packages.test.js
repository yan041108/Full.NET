import { describe, expect, it, vi } from 'vitest';
import { createTenantPackagesController } from '../js/core/tenant-packages.js';

describe('Layui Host 租户套餐控制器', () => {
  it('加载目录并提交创建表单', async () => {
    document.body.innerHTML = `
      <form data-tenant-packages-create-form>
        <input name="code">
        <input name="name">
        <input name="description">
      </form>
      <div data-tenant-packages-problem hidden><strong></strong><span></span></div>
      <div data-tenant-packages-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'package-1',
          code: 'standard',
          name: '标准版',
          description: '默认套餐',
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'package-2',
        code: 'pro',
        name: '专业版',
        description: null,
        isActive: true,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'package-2',
          code: 'pro',
          name: '专业版',
          description: null,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createTenantPackagesController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="code"]').value = 'pro';
    document.querySelector('[name="name"]').value = '专业版';

    await controller.load();
    expect(document.querySelector('[data-tenant-packages-directory] code')?.textContent)
      .toBe('standard');

    document.querySelector('[data-tenant-packages-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/tenancy/tenant-packages',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          code: 'pro',
          name: '专业版',
          description: null
        })
      })
    );
    controller.dispose();
  });
});
