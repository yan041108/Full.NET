import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  listCodeGenerationCatalogColumns,
  listCodeGenerationCatalogTables,
  syncCodeGenerationCatalogColumns
} from './code-generation-catalog';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Vue 代码生成目录 API', () => {
  it('使用稳定路径读取表目录、列配置并同步人工 UI', async () => {
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
    const responses = [
      jsonResponse([{ tableName: 'fn_codegeneration_template' }]),
      jsonResponse({
        tableName: 'fn_codegeneration_template',
        columns: [column],
        skippedColumnNames: []
      }),
      jsonResponse({
        tableName: 'fn_codegeneration_template',
        columns: [column],
        addedColumnNames: ['Id'],
        removedColumnNames: [],
        skippedColumnNames: []
      })
    ];
    const fetchMock = vi.fn().mockImplementation(
      () => Promise.resolve(responses.shift())
    );
    vi.stubGlobal('fetch', fetchMock);

    await listCodeGenerationCatalogTables();
    await listCodeGenerationCatalogColumns('fn_codegeneration_template');
    await syncCodeGenerationCatalogColumns(
      'fn_codegeneration_template',
      [column]
    );

    expect(fetchMock.mock.calls.map(call => {
      const url = new URL(call[0], 'http://localhost');
      return [`${url.pathname}${url.search}`, call[1]?.method ?? 'GET'];
    })).toEqual([
      ['/api/v1/code-generation/catalog/tables', 'GET'],
      ['/api/v1/code-generation/catalog/tables/fn_codegeneration_template/columns', 'GET'],
      ['/api/v1/code-generation/catalog/column-sync', 'POST']
    ]);
  });
});

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' }
  });
}
