import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { cancelReportingExportTask } from './reporting-export-tasks';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const canceled = () => ({ id, definitionId: id, definitionKey: 'report', definitionName: 'Report', versionNumber: 7,
  formatKey: 'excel', statusKey: 'canceled', rowCount: 0, outputFileName: null, errorCode: null, errorMessage: null,
  requestedByUserId: id, createdAtUtc: '2026-11-03T00:00:00Z', completedAtUtc: '2026-11-03T00:00:01Z',
  version: 6, outputFileId: null, parameters: [{ parameterKey: 'topN', value: '20' }] });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('提交任务版本及取消信号，保留发布版本与参数', async () => {
  vi.mocked(http.request).mockResolvedValue(canceled()); const signal = new AbortController().signal;
  const result = await cancelReportingExportTask(id, 5, signal); expect(result.versionNumber).toBe(7); expect(result.parameters[0]?.value).toBe('20');
  const call = vi.mocked(http.request).mock.lastCall!; expect(call[0]).toBe(`/api/v1/reporting/export-tasks/${id}/cancel`);
  expect(call[1]?.method).toBe('POST'); expect(JSON.parse(String(call[1]?.body))).toEqual({ version: 5 }); expect(call[2]).toBe(signal);
});
it('未取消、旧版本、缺少版本或其他目标的响应不能显示成功', async () => {
  for (const value of [{ ...canceled(), statusKey: 'queued' }, { ...canceled(), version: 4 }, { ...canceled(), version: undefined },
    { ...canceled(), id: '019bc2b1-2a40-7cc3-8992-a80de51bf331' }]) {
    vi.mocked(http.request).mockResolvedValue(value); await expect(cancelReportingExportTask(id, 5)).rejects.toThrow();
  }
});
it('领取冲突原样上抛且不重放写入', async () => {
  const error = { status: 409, code: 'reporting.export_task.cancel_conflict' }; vi.mocked(http.request).mockRejectedValue(error);
  await expect(cancelReportingExportTask(id, 5)).rejects.toEqual(error); expect(http.request).toHaveBeenCalledTimes(1);
});
