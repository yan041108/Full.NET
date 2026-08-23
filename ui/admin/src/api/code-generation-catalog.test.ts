import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  listCodeGenerationCatalogColumns,
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
});
