import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { previewCodeGeneration } from './code-generation-previews';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const previewResponse = {
  databaseTableName: 'acme_catalog_product',
  readPermission: 'catalog.products.read',
  writePermission: 'catalog.products.write',
  artifacts: []
};

const previewRequest = {
  ownerKey: 'acme',
  moduleKey: 'catalog',
  entityKey: 'product',
  databaseTableName: 'acme_catalog_product',
  rootNamespace: 'Acme.Modules.Catalog',
  clrTypeName: 'Product',
  apiResourceName: 'products',
  permissionResourceName: 'products',
  dataScope: 'TenantRequired' as const,
  hasVersion: false,
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
};

describe('code-generation-previews api', () => {
  beforeEach(() => requestMock.mockReset());

  it('submits explicit schema with JSON content type', async () => {
    requestMock.mockResolvedValueOnce(previewResponse);

    await expect(previewCodeGeneration(previewRequest))
      .resolves.toMatchObject({ databaseTableName: 'acme_catalog_product' });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/code-generation/previews',
      expect.objectContaining({
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(previewRequest)
      }),
      undefined
    );
  });
});
