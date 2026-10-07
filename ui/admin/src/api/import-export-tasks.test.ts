import { beforeEach, describe, expect, it, vi } from 'vitest';
import { importExportCreateImportTask, importExportDownloadStaticSchemaTemplate } from '@fullnet/client-contracts';
import { createImportExportTask, downloadStaticImportTemplate } from './import-export-tasks';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(), importExportCreateImportTask: vi.fn(), importExportDownloadStaticSchemaTemplate: vi.fn()
}));
vi.mock('./http', () => ({ http: {} }));
beforeEach(() => vi.resetAllMocks());
describe('导入 SDK 包装', () => {
  it('上传保留原文件和取消信号，不手工覆盖 multipart Content-Type', async () => {
    const file = new File(['fixture'], 'positions.xlsx'); const signal = new AbortController().signal;
    vi.mocked(importExportCreateImportTask).mockResolvedValue(importTask);
    expect(await createImportExportTask(importTask.schemaKey, 'positions', file, signal)).toEqual(importTask);
    expect(importExportCreateImportTask).toHaveBeenCalledWith({}, { schemaKey: importTask.schemaKey, worksheetKey: 'positions', file }, signal);
  });
  it('模板下载传递所选键和信号，拒绝非 Blob', async () => {
    const signal = new AbortController().signal; const blob = new Blob(['fixture']);
    vi.mocked(importExportDownloadStaticSchemaTemplate).mockResolvedValueOnce(blob).mockResolvedValueOnce({} as Blob);
    expect(await downloadStaticImportTemplate(importTask.schemaKey, 'positions', signal)).toBe(blob);
    expect(importExportDownloadStaticSchemaTemplate).toHaveBeenCalledWith({}, { schemaKey: importTask.schemaKey, worksheetKey: 'positions' }, signal);
    await expect(downloadStaticImportTemplate(importTask.schemaKey, 'positions', signal)).rejects.toThrow('client.invalid_import_export_template');
  });
});
