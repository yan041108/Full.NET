import { describe, expect, it, vi } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  createCodeGenerationPreviewsController
} from '../js/core/code-generation-previews.js';

describe('Layui 代码生成模板工作台', () => {
  it('模板目录与写入表单使用独立权限边界', () => {
    const html = readFileSync(
      resolve(process.cwd(), 'index.html'),
      'utf8'
    );
    const page = new DOMParser().parseFromString(html, 'text/html');
    const panel = page.querySelector('[data-codegen-templates]');
    const directory = page.querySelector(
      '[data-codegen-template-directory]'
    );
    const form = page.querySelector('[data-codegen-template-form]');

    expect(panel?.hasAttribute('data-permission')).toBe(false);
    expect(directory?.getAttribute('data-permission'))
      .toBe('codegen.templates.read');
    expect(form?.getAttribute('data-permission'))
      .toBe('codegen.templates.write');
  });

  it('按模板读写权限加载、更新并软删除所选模板', async () => {
    document.body.innerHTML = `
      <div data-codegen-problem hidden><strong></strong><span></span></div>
      <section data-codegen-templates>
        <form data-codegen-template-form>
          <input name="templateName" />
          <textarea name="templateDescription"></textarea>
          <button type="submit">save</button>
          <button type="button" data-codegen-template-update>update</button>
          <button type="button" data-codegen-template-delete>delete</button>
        </form>
        <div data-codegen-template-directory></div>
      </section>
      <form data-codegen-form>
        <textarea name="schema"></textarea>
        <button type="submit">preview</button>
      </form>
      <div data-codegen-summary></div>
      <div data-codegen-artifacts></div>
      <pre data-codegen-content><code></code></pre>
    `;
    const template = createTemplate();
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [template],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce({ ...template, name: 'Updated', version: 2 })
      .mockResolvedValueOnce(undefined);
    const controller = createCodeGenerationPreviewsController(document, {
      request,
      translation: () => ({ t: key => key }),
      hasPermission: () => true
    });

    await controller.load();
    document.querySelector('[data-codegen-template-load]').click();
    expect(document.querySelector('[name="schema"]').value)
      .toContain('"HostOnly"');
    document.querySelector('[name="templateName"]').value = 'Updated';
    document.querySelector('[data-codegen-template-update]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(
      document.querySelector('[data-codegen-template-directory]').textContent
    ).toContain('v2'));
    expect(request.mock.calls[1][0]).toBe(
      `/api/v1/code-generation/templates/${template.id}`
    );
    expect(JSON.parse(request.mock.calls[1][1].body).version).toBe(1);

    document.querySelector('[data-codegen-template-delete]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));
    expect(request.mock.calls[2][0]).toBe(
      `/api/v1/code-generation/templates/${template.id}/delete`
    );
    controller.dispose();
  });
});

function createTemplate() {
  return {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
    name: 'Product CRUD',
    description: null,
    schema: {
      ownerKey: 'acme',
      moduleKey: 'catalog',
      entityKey: 'product',
      databaseTableName: 'acme_catalog_product',
      rootNamespace: 'Acme.Modules.Catalog',
      clrTypeName: 'Product',
      apiResourceName: 'products',
      permissionResourceName: 'products',
      dataScope: 'HostOnly',
      hasVersion: true,
      columns: [{
        databaseName: 'Id',
        clrPropertyName: 'Id',
        jsonPropertyName: 'id',
        scalarType: 'Uuid',
        isNullable: false,
        maxLength: null,
        numericPrecision: null,
        numericScale: null
      }]
    },
    schemaSha256: 'a'.repeat(64),
    createdAtUtc: '2026-07-30T08:00:00Z',
    createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
    updatedAtUtc: null,
    updatedByUserId: null,
    version: 1
  };
}
