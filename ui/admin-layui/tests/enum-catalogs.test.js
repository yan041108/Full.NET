import { describe, expect, it, vi } from 'vitest';
import { createEnumCatalogsController } from '../js/core/enum-catalogs.js';

describe('Layui 枚举目录控制器', () => {
  it('加载目录并查看成员', async () => {
    document.body.innerHTML = `
      <div data-enum-catalogs-problem hidden><strong></strong><span></span></div>
      <div data-enum-catalogs-directory></div>
      <div data-enum-catalogs-members></div>
    `;
    const request = vi.fn()
      .mockResolvedValueOnce([{
        key: 'settings.config_value_kind',
        displayName: '配置值类型',
        description: null,
        memberCount: 1
      }])
      .mockResolvedValueOnce({
        key: 'settings.config_value_kind',
        displayName: '配置值类型',
        description: null,
        members: [{ code: 'string', label: 'string', displayOrder: 0 }]
      });

    const controller = createEnumCatalogsController(document, {
      request,
      translation: () => ({
        t: (key, params) => (params ? `${key}:${JSON.stringify(params)}` : key)
      })
    });

    await controller.load();
    expect(document.querySelector('[data-enum-catalogs-directory] code')?.textContent)
      .toBe('settings.config_value_kind');

    document.querySelector('[data-enum-catalogs-select]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    expect(document.querySelector('[data-enum-catalogs-members] code')?.textContent)
      .toBe('string');
    controller.dispose();
  });
});
