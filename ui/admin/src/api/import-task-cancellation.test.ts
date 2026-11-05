import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { cancelImportExportTask } from './import-export-tasks';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const canceled = () => ({ id, tenantId: id, schemaKey: 'source', schemaDisplayName: 'Source', worksheetKey: 'rows', sourceFileId: id,
  sourceRetention: { expiresAtUtc: '2027-01-01T00:00:00Z', stateKey: 'available', releasedAtUtc: null },
  sourceFileName: 'rows.xlsx', statusKey: 'canceled', totalRows: 2, validRowCount: 2, invalidRowCount: 0, errorCode: null,
  requestedByUserId: id, createdAtUtc: '2026-11-03T00:00:00Z', previewCompletedAtUtc: null, processedRowCount: 1, succeededRowCount: 1,
  executionFailedRowCount: 0, nextLineNumber: 1, executionStartedAtUtc: null, executionCompletedAtUtc: '2026-11-03T00:00:01Z',
  hasErrorReceipt: false, version: 6, previewRows: [], columnMappings: null });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('发送已读取的任务版本和取消信号，接受保留原进度的已取消详情', async () => {
  vi.mocked(http.request).mockResolvedValue(canceled()); const signal = new AbortController().signal;
  expect((await cancelImportExportTask(id, 5, signal)).processedRowCount).toBe(1);
  const call = vi.mocked(http.request).mock.lastCall!; expect(call[0]).toBe(`/api/v1/import-export/tasks/${id}/cancel`);
  expect(call[1]?.method).toBe('POST'); expect(JSON.parse(String(call[1]?.body))).toEqual({ version: 5 }); expect(call[2]).toBe(signal);
});
it('来源未取消、旧版本或其他目标的响应不能显示成功', async () => {
  for (const value of [{ ...canceled(), statusKey: 'queued' }, { ...canceled(), version: 4 }, { ...canceled(), id: '019bc2b1-2a40-7cc3-8992-a80de51bf331' }]) {
    vi.mocked(http.request).mockResolvedValue(value); await expect(cancelImportExportTask(id, 5)).rejects.toThrow();
  }
});
it('冲突原样上抛并且不重放取消写入', async () => {
  vi.mocked(http.request).mockRejectedValue({ status: 409, code: 'import_export.task.cancel_conflict' });
  await expect(cancelImportExportTask(id, 5)).rejects.toEqual({ status: 409, code: 'import_export.task.cancel_conflict' }); expect(http.request).toHaveBeenCalledTimes(1);
});
