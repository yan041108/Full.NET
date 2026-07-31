import { describe, expect, it } from 'vitest';
import {
  isCodeGenerationTemplatePage,
  isCodeGenerationTemplateResponse
} from '../src/code-generation-templates';

describe('code-generation template contracts', () => {
  const schema = {
    ownerKey: 'acme',
    moduleKey: 'catalog',
    entityKey: 'product',
    databaseTableName: 'acme_catalog_product',
    rootNamespace: 'Acme.Modules.Catalog',
    clrTypeName: 'Product',
    apiResourceName: 'products',
    permissionResourceName: 'products',
    dataScope: 'host.only',
    hasVersion: true,
    columns: [{
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'uuid',
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }]
  };
  const template = {
    id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
    name: 'Product CRUD',
    description: null,
    schema,
    schemaSha256: 'a'.repeat(64),
    createdAtUtc: '2026-07-30T08:00:00+00:00',
    createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
    updatedAtUtc: null,
    updatedByUserId: null,
    version: 1
  };

  it('accepts a canonical template and page', () => {
    expect(isCodeGenerationTemplateResponse(template)).toBe(true);
    expect(isCodeGenerationTemplatePage({
      items: [template],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('rejects malformed hashes, schemas, audit pairs, and pages', () => {
    expect(isCodeGenerationTemplateResponse({
      ...template,
      schemaSha256: 'ABC'
    })).toBe(false);
    expect(isCodeGenerationTemplateResponse({
      ...template,
      schema: { ...schema, dataScope: 'implicit' }
    })).toBe(false);
    expect(isCodeGenerationTemplateResponse({
      ...template,
      updatedAtUtc: '2026-07-30T09:00:00Z',
      updatedByUserId: null
    })).toBe(false);
    expect(isCodeGenerationTemplatePage({
      items: [template],
      page: 0,
      pageSize: 101,
      total: -1
    })).toBe(false);
  });
});
