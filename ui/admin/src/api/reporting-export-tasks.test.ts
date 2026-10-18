import { beforeEach, describe, expect, it, vi } from 'vitest';
import { reportingCancelExportTask } from '@fullnet/client-contracts';
import { cancelReportingExportTask, getReportingExportTask } from './reporting-export-tasks';
import { request } from './http';
import { exportTask } from '../test/data-output-fixtures';
vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(), reportingCancelExportTask: vi.fn()
}));
vi.mock('./http', () => ({ http: {}, request: vi.fn(), requestBlob: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('报表取消 SDK 适配', () => {
  it('透传任务和取消信号，只接受该任务的取消终态', async () => {
    const cancelled = { ...exportTask, statusKey: 'cancelled' }; const signal = new AbortController().signal;
    vi.mocked(reportingCancelExportTask).mockResolvedValue(cancelled);
    expect(await cancelReportingExportTask(exportTask.id, signal)).toEqual(cancelled);
    expect(reportingCancelExportTask).toHaveBeenCalledWith({}, { taskId: exportTask.id }, signal);
  });
  it.each(['identity', 'status', 'shape'])('拒绝%s不符的取消结果', async mismatch => {
    const result = mismatch === 'shape' ? {} : { ...exportTask, statusKey: mismatch === 'status' ? 'queued' : 'cancelled',
      id: mismatch === 'identity' ? '01980000-0000-7000-8000-000000000003' : exportTask.id };
    vi.mocked(reportingCancelExportTask).mockResolvedValue(result as typeof exportTask);
    await expect(cancelReportingExportTask(exportTask.id)).rejects.toThrow('client.invalid_reporting_export_task_cancellation');
  });
  it('接受同一 UUID 的大小写等价文本，已取消结果不能被误拒绝', async () => {
    vi.mocked(reportingCancelExportTask).mockResolvedValue({ ...exportTask, statusKey: 'cancelled' });
    expect((await cancelReportingExportTask(exportTask.id.toUpperCase())).statusKey).toBe('cancelled');
  });
  it('保留服务端冲突错误，不把领取失败当成取消成功', async () => {
    const conflict = { title: 'claimed', status: 409, code: 'reporting.export_task.status_invalid' };
    vi.mocked(reportingCancelExportTask).mockRejectedValue(conflict);
    await expect(cancelReportingExportTask(exportTask.id)).rejects.toBe(conflict);
  });
});

describe('报表导出详情响应归属', () => {
  it('透传取消信号并接受等价 UUID 的大小写', async () => {
    vi.mocked(request).mockResolvedValue(exportTask); const signal = new AbortController().signal;
    expect(await getReportingExportTask(exportTask.id.toUpperCase(), signal)).toEqual(exportTask);
    expect(request).toHaveBeenCalledWith(`/api/v1/reporting/export-tasks/${exportTask.id.toUpperCase()}`, { method: 'GET' }, signal);
  });
  it.each([{}, { ...exportTask, id: '01980000-0000-7000-8000-000000000003' }])('拒绝无效或其它任务的详情 %j', async value => {
    vi.mocked(request).mockResolvedValue(value); await expect(getReportingExportTask(exportTask.id)).rejects.toThrow('client.invalid_reporting_export_task_detail');
  });
});
