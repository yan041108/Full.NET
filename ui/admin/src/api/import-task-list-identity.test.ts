import { beforeEach, describe, expect, it, vi } from 'vitest';
import { importExportListImportTasks } from '@fullnet/client-contracts';
import { listImportExportTasks } from './import-export-tasks';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('@fullnet/client-contracts', async original => ({ ...await original<typeof import('@fullnet/client-contracts')>(), importExportListImportTasks: vi.fn() }));
vi.mock('./http', () => ({ http: {} }));
beforeEach(() => vi.resetAllMocks());
describe('导入任务列表查询归属', () => {
  it.each([
    { items: [importTask], page: 2, pageSize: 20, total: 1 },
    { items: [importTask], page: 1, pageSize: 10, total: 1 },
    { items: [importTask, { ...importTask, id: importTask.id.toUpperCase() }], page: 1, pageSize: 20, total: 2 },
    { items: [{ ...importTask, id: '00000000-0000-0000-0000-000000000000' }], page: 1, pageSize: 20, total: 1 }
  ])('拒绝错误分页、重复或空身份 %j', async response => {
    vi.mocked(importExportListImportTasks).mockResolvedValue(response); await expect(listImportExportTasks()).rejects.toThrow(/^client.invalid_import_export_task_page$/u);
  });
  it('冻结页码和Schema参数并保留取消信号，筛选比较遵循数据库语义', async () => {
    const signal = new AbortController().signal;
    vi.mocked(importExportListImportTasks).mockResolvedValue({ items: [importTask], page: 1, pageSize: 20, total: 1 });
    await expect(listImportExportTasks(1, 20, importTask.schemaKey.toUpperCase(), signal)).resolves.toMatchObject({ items: [importTask] });
    expect(importExportListImportTasks).toHaveBeenCalledWith({}, { page: 1, pageSize: 20, schemaKey: importTask.schemaKey.toUpperCase() }, signal);
  });
});
