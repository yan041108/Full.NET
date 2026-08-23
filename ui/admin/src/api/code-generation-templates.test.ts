import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  getCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from './code-generation-templates';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const template = {
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

describe('code-generation-templates api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists, reads, creates, updates and deletes templates via stable paths', async () => {
    requestMock
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

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/code-generation/templates?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/code-generation/templates/${template.id}`,
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/code-generation/templates',
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      4,
      `/api/v1/code-generation/templates/${template.id}`,
      expect.objectContaining({ method: 'PUT' }),
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      5,
      `/api/v1/code-generation/templates/${template.id}/delete`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 2 })
      }),
      undefined
    );
  });
});
