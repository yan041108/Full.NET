import { describe, expect, it, vi } from 'vitest';
import { createTenantDictTypesController } from '../js/core/tenant-dict-types.js';

describe('Layui 租户数据字典控制器', () => {
  it('只读用户只能查看字典类型和字典项', async () => {
    document.body.innerHTML = `
      <div data-tenant-dict-types-problem hidden><strong></strong><span></span></div>
      <form data-tenant-dict-types-create-form hidden></form>
      <div data-tenant-dict-types-directory></div>
      <section data-tenant-dict-items-panel hidden>
        <h2 data-tenant-dict-items-panel-title></h2>
        <button type="button" data-tenant-dict-items-close></button>
        <form data-tenant-dict-items-create-form hidden></form>
        <div data-tenant-dict-items-directory></div>
      </section>`;
    const dictType = {
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf29e',
      code: 'readonly_status',
      name: '只读状态',
      description: null,
      displayOrder: 1,
      isActive: true,
      version: 1
    };
    const dictItem = {
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf29f',
      dictTypeId: dictType.id,
      label: '只读项',
      value: 'readonly',
      color: null,
      displayOrder: 1,
      isActive: true,
      version: 1
    };
    const request = vi.fn(async path => path.includes('/items?')
      ? { items: [dictItem], page: 1, pageSize: 20, total: 1 }
      : { items: [dictType], page: 1, pageSize: 20, total: 1 });
    const controller = createTenantDictTypesController(document, {
      request,
      translation: () => ({ locale: 'zh-CN', t: key => key }),
      canWrite: () => false
    });

    await controller.load();
    document.querySelector('[data-tenant-dict-types-items]')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));

    expect(document.querySelector('[data-tenant-dict-types-create-form]')?.hidden).toBe(true);
    expect(document.querySelector('[data-tenant-dict-items-create-form]')?.hidden).toBe(true);
    expect(document.querySelector('[data-tenant-dict-types-edit]')).toBeNull();
    expect(document.querySelector('[data-tenant-dict-types-disable]')).toBeNull();
    expect(document.querySelector('[data-tenant-dict-items-edit]')).toBeNull();
    expect(document.querySelector('[data-tenant-dict-items-disable]')).toBeNull();
    expect(document.querySelector('[data-tenant-dict-items-directory]')?.textContent)
      .toContain('只读项');
    controller.dispose();
  });
});
