import { beforeEach, describe, expect, it, vi } from 'vitest';
import { importExportCreateImportTask, importExportDownloadStaticSchemaTemplate, importExportGetImportTask,
  importExportExecuteImportTask, importExportResumeImportTask, importExportRetryImportTask } from '@fullnet/client-contracts';
import { createImportExportTask, downloadStaticImportTemplate, getImportExportTask,
  executeImportExportTask, resumeImportExportTask, retryImportExportTask } from './import-export-tasks';
import { importTask } from '../test/task-lifecycle-fixtures';
vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(), importExportCreateImportTask: vi.fn(), importExportDownloadStaticSchemaTemplate: vi.fn(),
  importExportGetImportTask:vi.fn(),importExportExecuteImportTask:vi.fn(),importExportResumeImportTask:vi.fn(),importExportRetryImportTask:vi.fn()
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

describe('导入任务响应归属', () => {
  const operations = [
    ['详情', getImportExportTask, importExportGetImportTask],
    ['执行', executeImportExportTask, importExportExecuteImportTask],
    ['恢复', resumeImportExportTask, importExportResumeImportTask],
    ['重试', retryImportExportTask, importExportRetryImportTask]
  ] as const;
  it.each(operations)('%s拒绝结构正确但属于另一个任务的结果', async (_, operation, generated) => {
    vi.mocked(generated).mockResolvedValue({...importTask,id:'019bc2b1-2a40-7cc3-8992-a80de51bf298'});
    await expect(operation(importTask.id)).rejects.toThrow('client.invalid_import_export_task_identity');
  });
  it.each(operations)('%s保留取消信号并接受同一UUID的大小写形式', async (_, operation, generated) => {
    const signal=new AbortController().signal;vi.mocked(generated).mockResolvedValue(importTask);
    expect(await operation(importTask.id.toUpperCase(),signal)).toEqual(importTask);
    expect(generated).toHaveBeenCalledWith({}, {taskId:importTask.id.toUpperCase()}, signal);
  });
  it.each(operations)('%s优先拒绝畸形响应而非只核对任务ID', async (_, operation, generated) => {
    vi.mocked(generated).mockResolvedValue({...importTask,id:'019bc2b1-2a40-7cc3-8992-a80de51bf298',previewRows:null} as unknown as typeof importTask);
    await expect(operation(importTask.id)).rejects.toThrow(/^client\.invalid_import_export_task$/u);
  });
  it.each(['schemaKey','worksheetKey'] as const)('创建拒绝不匹配的%s', async field => {
    vi.mocked(importExportCreateImportTask).mockResolvedValue({...importTask,[field]:'another'});
    await expect(createImportExportTask(importTask.schemaKey,importTask.worksheetKey,new File(['fixture'],'positions.xlsx')))
      .rejects.toThrow('client.invalid_import_export_task_identity');
  });
  it('创建结果按服务端边界去除键的首尾空白，仍保持机器键大小写', async () => {
    vi.mocked(importExportCreateImportTask).mockResolvedValue(importTask);
    expect(await createImportExportTask(' '+importTask.schemaKey+' ', ' positions ', new File(['fixture'],'positions.xlsx'))).toEqual(importTask);
    await expect(createImportExportTask(importTask.schemaKey.toUpperCase(),importTask.worksheetKey,new File(['fixture'],'positions.xlsx')))
      .rejects.toThrow('client.invalid_import_export_task_identity');
  });
  it('接受.NET Trim原本支持的NEL与空格混合边界',async()=>{
    vi.mocked(importExportCreateImportTask).mockResolvedValue(importTask);
    expect(await createImportExportTask(' \u0085 '+importTask.schemaKey+' \u0085 ', '\u0085 positions \u0085', new File(['fixture'],'positions.xlsx'))).toEqual(importTask);
  });
  it('BOM不是服务端可裁剪空白，不能将错配结果当作成功',async()=>{
    vi.mocked(importExportCreateImportTask).mockResolvedValue(importTask);
    await expect(createImportExportTask('\uFEFF'+importTask.schemaKey,importTask.worksheetKey,new File(['fixture'],'positions.xlsx')))
      .rejects.toThrow('client.invalid_import_export_task_identity');
  });
});
