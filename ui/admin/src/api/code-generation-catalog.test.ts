import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  generateCodeGenerationCatalogMigrationDraft,
  getCodeGenerationCatalogMetadata,
  listCodeGenerationCatalogColumns,
  listCodeGenerationCatalogObjects,
  listCodeGenerationCatalogTables,
  syncCodeGenerationCatalogColumns
} from './code-generation-catalog';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const column = {
  databaseName: 'Name',
  clrPropertyName: 'Name',
  jsonPropertyName: 'name',
  scalarType: 'string' as const,
  isNullable: false,
  maxLength: 128,
  numericPrecision: null,
  numericScale: null,
  ui: {
    controlKind: 'text' as const,
    showInList: true,
    includeInCreate: true,
    includeInUpdate: true,
    required: true,
    sortable: true,
    queryable: true,
    queryKind: 'contains' as const,
    unique: false,
    includeInImportExport: true
  }
};

describe('code-generation-catalog api', () => {
  beforeEach(() => requestMock.mockReset());

  it('reads catalog tables, columns and syncs manual UI metadata', async () => {
    requestMock
      .mockResolvedValueOnce([{ tableName: 'fn_codegeneration_template' }])
      .mockResolvedValueOnce({
        tableName: 'fn_codegeneration_template',
        columns: [column],
        skippedColumnNames: []
      })
      .mockResolvedValueOnce({
        tableName: 'fn_codegeneration_template',
        columns: [column],
        addedColumnNames: ['Id'],
        removedColumnNames: [],
        skippedColumnNames: []
      });

    await listCodeGenerationCatalogTables();
    await listCodeGenerationCatalogColumns('fn_codegeneration_template');
    await syncCodeGenerationCatalogColumns(
      'fn_codegeneration_template',
      [column]
    );

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/code-generation/catalog/tables',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/code-generation/catalog/tables/fn_codegeneration_template/columns',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/code-generation/catalog/column-sync',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          tableName: 'fn_codegeneration_template',
          columns: [column]
        })
      }),
      undefined
    );
  });

  it('reads catalog objects, metadata and migration draft', async () => {
    requestMock
      .mockResolvedValueOnce([
        { objectName: 'fn_codegeneration_template', objectKind: 'table' }
      ])
      .mockResolvedValueOnce({
        objectName: 'fn_codegeneration_template',
        objectKind: 'table',
        columns: [{
          columnName: 'Id',
          dataType: 'uniqueidentifier',
          columnType: 'uniqueidentifier',
          isNullable: false,
          maxLength: null,
          ordinalPosition: 1,
          numericPrecision: null,
          numericScale: null
        }]
      })
      .mockResolvedValueOnce({
        tableName: 'fn_codegeneration_template',
        sqlServerDraft: '-- sql server',
        mySqlDraft: '-- mysql',
        warnings: []
      });

    await listCodeGenerationCatalogObjects();
    await getCodeGenerationCatalogMetadata('fn_codegeneration_template');
    await generateCodeGenerationCatalogMigrationDraft('fn_codegeneration_template');

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/code-generation/catalog/objects',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/code-generation/catalog/objects/fn_codegeneration_template/metadata',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/code-generation/catalog/migration-draft',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ tableName: 'fn_codegeneration_template' })
      }),
      undefined
    );
  });
});

