import { describe, expect, it, vi } from 'vitest';
import {
  createCodeGenerationTemplatesApi
} from '../js/core/code-generation-templates.js';

describe('Layui 代码生成模板 API', () => {
  it('验证响应并使用稳定的增删改查路径', async () => {
    const template = createTemplate();
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [template],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce(template)
      .mockResolvedValueOnce(template)
      .mockResolvedValueOnce({ ...template, version: 2 })
      .mockResolvedValueOnce(undefined);
    const api = createCodeGenerationTemplatesApi(request);

    await api.list();
    await api.get(template.id);
    await api.create({
      name: template.name,
      description: null,
      schema: template.schema
    });
    await api.update(template.id, {
      name: template.name,
      description: null,
      schema: template.schema,
      version: 1
    });
    await api.remove(template.id, 2);

    expect(request.mock.calls.map(([path, options]) => [
      path,
      options?.method ?? 'GET'
    ])).toEqual([
      ['/api/v1/code-generation/templates?page=1&pageSize=20', 'GET'],
      [`/api/v1/code-generation/templates/${template.id}`, 'GET'],
      ['/api/v1/code-generation/templates', 'POST'],
      [`/api/v1/code-generation/templates/${template.id}`, 'PUT'],
      [`/api/v1/code-generation/templates/${template.id}/delete`, 'POST']
    ]);
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
