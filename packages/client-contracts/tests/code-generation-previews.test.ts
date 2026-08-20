import { describe, expect, it } from 'vitest';
import {
  isCodeGenerationPreviewRequest,
  isCodeGenerationPreviewResponse
} from '../src/code-generation-previews';

describe('code-generation preview contracts', () => {
  const request = {
    ownerKey: 'acme',
    moduleKey: 'catalog',
    entityKey: 'product',
    databaseTableName: 'acme_catalog_product',
    rootNamespace: 'Acme.Modules.Catalog',
    clrTypeName: 'Product',
    apiResourceName: 'products',
    permissionResourceName: 'products',
    dataScope: 'TenantRequired',
    hasVersion: true,
    columns: [
      {
        databaseName: 'Id',
        clrPropertyName: 'Id',
        jsonPropertyName: 'id',
        scalarType: 'Uuid',
        isNullable: false,
        maxLength: null,
        numericPrecision: null,
        numericScale: null
      }
    ]
  };

  const response = {
    databaseTableName: 'acme_catalog_product',
    readPermission: 'catalog.products.read',
    writePermission: 'catalog.products.write',
    artifacts: [
      {
        path: 'backend/ProductContracts.g.cs',
        kind: 'backend',
        sha256: 'a'.repeat(64),
        content: 'namespace Acme.Modules.Catalog.Generated;\n'
      }
    ]
  };

  it('accepts explicit schema inputs and stable artifact responses', () => {
    expect(isCodeGenerationPreviewRequest(request)).toBe(true);
    expect(isCodeGenerationPreviewResponse(response)).toBe(true);
    expect(isCodeGenerationPreviewResponse({
      ...response,
      artifacts: [{ ...response.artifacts[0], kind: 'openapi_contract' }]
    })).toBe(true);
  });

  it('accepts canonical entity capabilities and scene machine codes', () => {
    const { hasVersion: _, ...schema } = request;

    expect(isCodeGenerationPreviewRequest({
      ...schema,
      dataScope: 'tenant.required',
      entityCapabilities: {
        deleteMode: 'soft.delete',
        hasCreatedAudit: true,
        hasUpdatedAudit: true,
        hasDeletedAudit: true,
        hasVersion: true,
        ownershipMode: 'none'
      },
      scene: 'single',
      relationships: [],
      columns: request.columns.map(column => ({
        ...column,
        scalarType: column.scalarType === 'Uuid'
          ? 'uuid'
          : column.scalarType === 'String'
            ? 'string'
            : column.scalarType === 'Boolean'
              ? 'boolean'
              : 'int64'
      }))
    })).toBe(true);
  });

  it('accepts only the exact pre-1.0 aliases for explicit capabilities', () => {
    const { hasVersion: _, ...schema } = request;

    expect(isCodeGenerationPreviewRequest({
      ...schema,
      entityCapabilities: {
        deleteMode: 'HardDelete',
        hasCreatedAudit: false,
        hasUpdatedAudit: false,
        hasDeletedAudit: false,
        hasVersion: true,
        ownershipMode: 'None'
      },
      scene: 'Single',
      relationships: []
    })).toBe(true);
    expect(isCodeGenerationPreviewRequest({
      ...schema,
      entityCapabilities: {
        deleteMode: 'hardDelete',
        hasCreatedAudit: false,
        hasUpdatedAudit: false,
        hasDeletedAudit: false,
        hasVersion: true,
        ownershipMode: 'none'
      },
      scene: 'single',
      relationships: []
    })).toBe(false);
  });

  it('requires exactly one lifecycle capability shape', () => {
    expect(isCodeGenerationPreviewRequest({
      ...request,
      entityCapabilities: {
        deleteMode: 'hard.delete',
        hasCreatedAudit: false,
        hasUpdatedAudit: false,
        hasDeletedAudit: false,
        hasVersion: true,
        ownershipMode: 'none'
      },
      scene: 'single',
      relationships: []
    })).toBe(false);

    const { hasVersion: _, ...schema } = request;
    expect(isCodeGenerationPreviewRequest(schema)).toBe(false);
    expect(isCodeGenerationPreviewRequest({
      ...schema,
      entityCapabilities: {
        deleteMode: 'hard.delete',
        hasCreatedAudit: false,
        hasUpdatedAudit: false,
        hasDeletedAudit: false,
        hasVersion: true,
        ownershipMode: 'none'
      },
      scene: 'single',
      relationships: null
    })).toBe(false);
  });

  it('rejects unknown machine codes and malformed hashes', () => {
    expect(isCodeGenerationPreviewRequest({
      ...request,
      dataScope: 'ImplicitTenant'
    })).toBe(false);
    expect(isCodeGenerationPreviewRequest({
      ...request,
      columns: [{ ...request.columns[0], scalarType: 'Executable' }]
    })).toBe(false);
    expect(isCodeGenerationPreviewResponse({
      ...response,
      artifacts: [{ ...response.artifacts[0], sha256: 'ABC' }]
    })).toBe(false);
  });
});
