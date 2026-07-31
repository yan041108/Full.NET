import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  getCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from './code-generation-templates';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 代码生成模板 API', () => {
  it('使用稳定路径完成列表、详情、创建、更新和软删除', async () => {
    const template = createTemplate();
    const responses = [
      jsonResponse({ items: [template], page: 1, pageSize: 20, total: 1 }),
      jsonResponse(template),
      jsonResponse(template, 201),
      jsonResponse({ ...template, version: 2 }),
      new Response(null, { status: 204 })
    ];
    const fetchMock = vi.fn().mockImplementation(
      () => Promise.resolve(responses.shift())
    );
    vi.stubGlobal('fetch', fetchMock);

    await listCodeGenerationTemplates();
    await getCodeGenerationTemplate(template.id);
    await createCodeGenerationTemplate({
      name: template.name,
      description: null,
      schema: template.schema
    });
    await updateCodeGenerationTemplate(template.id, {
      name: template.name,
      description: null,
      schema: template.schema,
      version: 1
    });
    await deleteCodeGenerationTemplate(template.id, 2);

    expect(fetchMock.mock.calls.map(call => {
      const url = new URL(call[0], 'http://localhost');
      return [`${url.pathname}${url.search}`, call[1]?.method ?? 'GET'];
    })).toEqual([
      ['/api/v1/code-generation/templates?page=1&pageSize=20', 'GET'],
      [`/api/v1/code-generation/templates/${template.id}`, 'GET'],
      ['/api/v1/code-generation/templates', 'POST'],
      [`/api/v1/code-generation/templates/${template.id}`, 'PUT'],
      [`/api/v1/code-generation/templates/${template.id}/delete`, 'POST']
    ]);
  });
});

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' }
  });
}

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
      dataScope: 'HostOnly' as const,
      hasVersion: true,
      columns: [{
        databaseName: 'Id',
        clrPropertyName: 'Id',
        jsonPropertyName: 'id',
        scalarType: 'Uuid' as const,
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
