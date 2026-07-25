import { describe, expect, it, vi } from 'vitest';
import { createDictTypesController } from '../js/core/dict-types.js';

function mountDom() {
  document.body.innerHTML = `
    <form data-dict-types-create-form>
      <input name="code">
      <input name="name">
      <input name="description">
      <input name="displayOrder" value="10">
    </form>
    <div data-dict-types-problem hidden><strong></strong><span></span></div>
    <div data-dict-types-directory></div>
    <section data-dict-items-panel hidden>
      <h2 data-dict-items-panel-title></h2>
      <button type="button" data-dict-items-close></button>
      <form data-dict-items-create-form hidden>
        <input name="label">
        <input name="value">
        <input name="color">
        <input name="displayOrder" value="1">
      </form>
      <div data-dict-items-directory></div>
    </section>`;
}

describe('Layui Host 数据字典控制器', () => {
  it('加载目录并提交创建表单', async () => {
    mountDom();
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'dict-type-1',
          code: 'gender',
          name: '性别',
          description: '通用性别枚举',
          displayOrder: 10,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        id: 'dict-type-2',
        code: 'order_status',
        name: '订单状态',
        description: null,
        displayOrder: 10,
        isActive: true,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'dict-type-2',
          code: 'order_status',
          name: '订单状态',
          description: null,
          displayOrder: 10,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createDictTypesController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: (key, params) => params?.name ? `${key}:${params.name}` : key })
    });
    document.querySelector('[name="code"]').value = 'order_status';
    document.querySelector('[name="name"]').value = '订单状态';

    await controller.load();
    expect(document.querySelector('[data-dict-types-directory] code')?.textContent)
      .toBe('gender');
    expect(document.querySelector('[data-dict-types-items]')?.textContent)
      .toBe('dictItems.manage');

    document.querySelector('[data-dict-types-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/settings/dict-types',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          code: 'order_status',
          name: '订单状态',
          description: null,
          displayOrder: 10
        })
      })
    );
    controller.dispose();
  });

  it('选型后加载并创建字典项', async () => {
    mountDom();
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [{
          id: 'dict-type-1',
          code: 'gender',
          name: '性别',
          description: null,
          displayOrder: 10,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({
        items: [],
        page: 1,
        pageSize: 20,
        total: 0
      })
      .mockResolvedValueOnce({
        id: 'dict-item-1',
        dictTypeId: 'dict-type-1',
        label: '男',
        value: 'male',
        color: null,
        displayOrder: 1,
        isActive: true,
        version: 1
      })
      .mockResolvedValueOnce({
        items: [{
          id: 'dict-item-1',
          dictTypeId: 'dict-type-1',
          label: '男',
          value: 'male',
          color: null,
          displayOrder: 1,
          isActive: true,
          version: 1
        }],
        page: 1,
        pageSize: 20,
        total: 1
      });
    const controller = createDictTypesController(document, {
      request,
      translation: () => ({
        locale: 'zh-CN',
        t: (key, params) => (params?.name ? `${key}:${params.name}` : key)
      })
    });

    await controller.load();
    document.querySelector('[data-dict-types-items]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    expect(request).toHaveBeenNthCalledWith(
      2,
      '/api/v1/settings/dict-types/dict-type-1/items?page=1&pageSize=20'
    );
    expect(document.querySelector('[data-dict-items-panel]')?.hidden).toBe(false);
    expect(document.querySelector('[data-dict-items-panel-title]')?.textContent)
      .toBe('dictItems.panelTitle:gender');
    expect(document.querySelector('[data-dict-items-empty]')?.textContent)
      .toBe('dictItems.emptyDirectory');

    document.querySelector('[data-dict-items-create-form] [name="label"]').value = '男';
    document.querySelector('[data-dict-items-create-form] [name="value"]').value = 'male';
    document.querySelector('[data-dict-items-create-form]')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(4));
    expect(request).toHaveBeenNthCalledWith(
      3,
      '/api/v1/settings/dict-types/dict-type-1/items',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          label: '男',
          value: 'male',
          color: null,
          displayOrder: 1
        })
      })
    );
    expect(document.querySelector('[data-dict-items-directory] code')?.textContent)
      .toBe('male');
    controller.dispose();
  });
});
