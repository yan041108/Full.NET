import { afterEach, describe, expect, it, vi } from 'vitest';
import { previewCodeGeneration } from './code-generation-previews';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 代码生成预览 API', () => {
  it('使用 JSON content type 提交显式 Schema', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      databaseTableName: 'acme_catalog_product',
      readPermission: 'catalog.products.read',
      writePermission: 'catalog.products.write',
      artifacts: []
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    await previewCodeGeneration({
      ownerKey: 'acme',
      moduleKey: 'catalog',
      entityKey: 'product',
      databaseTableName: 'acme_catalog_product',
      rootNamespace: 'Acme.Modules.Catalog',
      clrTypeName: 'Product',
      apiResourceName: 'products',
      permissionResourceName: 'products',
      dataScope: 'TenantRequired',
      hasVersion: false,
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
    });

    const headers = new Headers(fetchMock.mock.calls[0][1].headers);
    expect(headers.get('content-type')).toBe('application/json');
  });
});
