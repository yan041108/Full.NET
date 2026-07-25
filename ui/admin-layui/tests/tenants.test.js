import { describe, expect, it, vi } from 'vitest';
import { createTenantsController } from '../js/core/tenants.js';

describe('Layui Host 租户控制器', () => {
  it('加载目录并提交开通表单', async () => {
    document.body.innerHTML = `
      <form data-tenants-create-form>
        <input name="identifier">
        <input name="name">
        <input name="domain">
      </form>
      <div data-tenants-problem hidden><strong></strong><span></span></div>
      <div data-tenants-directory></div>`;
    const request = vi.fn()
      .mockResolvedValueOnce({ items: [], page: 1, pageSize: 100, total: 0 })
      .mockResolvedValueOnce({
        items: [{
          id: 'tenant-1',
          identifier: 'acme',
          name: 'Acme Corporation',
          domain: 'acme.localhost',
          isActive: true,
          version: 1,
          defaultLocale: 'zh-CN'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'tenant-2',
        identifier: 'parity',
        name: '对等租户',
        domain: 'parity.localhost',
        isActive: true,
        version: 1,
        defaultLocale: 'zh-CN'
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'tenant-2',
          identifier: 'parity',
          name: '对等租户',
          domain: 'parity.localhost',
          isActive: true,
          version: 1,
          defaultLocale: 'zh-CN'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({ items: [], page: 1, pageSize: 100, total: 0 })
      .mockResolvedValueOnce({
        items: [{
          id: 'tenant-2',
          identifier: 'parity',
          name: '对等租户',
          domain: 'parity.localhost',
          isActive: true,
          version: 1,
          defaultLocale: 'zh-CN'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createTenantsController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key })
    });
    document.querySelector('[name="identifier"]').value = 'parity';
    document.querySelector('[name="name"]').value = '对等租户';
    document.querySelector('[name="domain"]').value = 'parity.localhost';

    await controller.load();
    expect(request).toHaveBeenNthCalledWith(
      1,
      '/api/v1/tenancy/tenant-packages?page=1&pageSize=100'
    );
    expect(document.querySelector('[data-tenants-directory] code')?.textContent)
      .toContain('acme.localhost');

    document.querySelector('[data-tenants-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(5));
    expect(request).toHaveBeenNthCalledWith(
      3,
      '/api/v1/tenancy/tenants',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          identifier: 'parity',
          name: '对等租户',
          domain: 'parity.localhost'
        })
      })
    );
    controller.dispose();
  });
});
