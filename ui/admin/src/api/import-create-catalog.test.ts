import { beforeEach, describe, expect, it, vi } from 'vitest';
import { importExportListStaticSchemas, importExportCreateImportTask } from '@fullnet/client-contracts';
import { listStaticImportSchemas, createImportExportTask } from './import-export-tasks';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('@fullnet/client-contracts', async original => ({ ...await original<typeof import('@fullnet/client-contracts')>(), importExportListStaticSchemas: vi.fn(), importExportCreateImportTask: vi.fn() }));
vi.mock('./http', () => ({ http: {} }));
const schema = { schemaKey: 'schema', displayName: 'Schema', scopeKey: 'tenant', requiredPermission: 'business.import', worksheets: [{ worksheetKey: 'sheet', displayName: 'Sheet', headerColumns: ['Code'] }] };
beforeEach(() => vi.resetAllMocks());
describe('导入目录和文件边界', () => {
  it.each([
    { rows: [schema, schema] }, { rows: [{ ...schema, schemaKey: '' }] }, { rows: [{ ...schema, requiredPermission: '' }] },
    { rows: [{ ...schema, worksheets: [schema.worksheets[0], schema.worksheets[0]] }] },
    { rows: [{ ...schema, worksheets: [{ ...schema.worksheets[0], worksheetKey: '' }] }] },
    { rows: [{ ...schema, worksheets: [null] }] }
  ])('拒绝有歧义或畸形的静态目录 %j', async ({ rows }) => {
    vi.mocked(importExportListStaticSchemas).mockResolvedValue(rows as unknown as typeof schema[]); await expect(listStaticImportSchemas()).rejects.toThrow(/^client.invalid_static_import_schema_list$/u);
  });
  it('目录机器键大小写不同保持独立，保留原响应', async () => {
    const rows = [schema, { ...schema, schemaKey: 'SCHEMA' }]; vi.mocked(importExportListStaticSchemas).mockResolvedValue(rows); await expect(listStaticImportSchemas()).resolves.toEqual(rows);
  });
  it.each([new File([], 'empty.xlsx'), new File(['a'], 'bad.csv'), new File([new Uint8Array(1024 * 1024 + 1)], 'large.xlsx')])('非法工作簿在请求前拒绝 %j', async file => {
    await expect(createImportExportTask('schema', 'sheet', file)).rejects.toThrow(/^client.invalid_import_export_file$/u); expect(importExportCreateImportTask).not.toHaveBeenCalled();
  });
  it('拒绝创建响应中的空任务身份', async () => {
    vi.mocked(importExportCreateImportTask).mockResolvedValue({ ...importTask, id: '00000000-0000-0000-0000-000000000000' }); await expect(createImportExportTask(importTask.schemaKey, importTask.worksheetKey, new File(['a'], 'positions.xlsx'))).rejects.toThrow(/^client.invalid_import_export_task_identity$/u);
  });
});
