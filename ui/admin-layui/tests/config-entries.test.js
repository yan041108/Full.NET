import { describe, expect, it, vi } from 'vitest';
import { createConfigEntriesController } from '../js/core/config-entries.js';

function mountFixture() {
  document.body.innerHTML = `
    <form data-config-entries-create-form>
      <input name="configKey" />
      <input name="displayName" />
      <input name="description" />
      <select name="valueKind"><option value="string" selected>string</option></select>
      <input name="value" />
      <input name="displayOrder" value="0" />
      <button type="submit">create</button>
    </form>
    <div data-config-entries-problem hidden><strong></strong><span></span></div>
    <div data-config-entries-directory></div>
  `;
}

describe('Layui 系统配置控制器', () => {
  it('加载列表并创建配置项', async () => {
    mountFixture();
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'config-1',
          configKey: 'system.title',
          displayName: '系统标题',
          description: null,
          valueKind: 'string',
          value: 'Full.NET',
          displayOrder: 1,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'config-2',
        configKey: 'ui.theme',
        displayName: '主题',
        description: null,
        valueKind: 'string',
        value: 'dark',
        displayOrder: 2,
        isActive: true,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [],
        page: 1,
        pageSize: 20,
        total: 0
      });

    const controller = createConfigEntriesController(document, {
      request,
      translation: () => ({
        t: (key, params) => (params ? `${key}:${JSON.stringify(params)}` : key)
      })
    });

    await controller.load();
    expect(document.querySelector('[data-config-entries-directory] code')?.textContent)
      .toBe('system.title');

    document.querySelector('[name="configKey"]').value = 'ui.theme';
    document.querySelector('[name="displayName"]').value = '主题';
    document.querySelector('[name="value"]').value = 'dark';
    document.querySelector('[data-config-entries-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/settings/config-entries',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          configKey: 'ui.theme',
          displayName: '主题',
          description: null,
          valueKind: 'string',
          value: 'dark',
          displayOrder: 0
        })
      })
    );

    controller.dispose();
  });
});
