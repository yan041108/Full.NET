import { beforeEach, describe, expect, it, vi } from 'vitest';
import { agentTaskItem, importTaskItem, reportingTaskItem, taskActions, taskPending, readMyTaskSource } from './myTasks';
import type { AiAgentRunResponse, ImportExportTaskResponse, ReportingExportTask } from '@fullnet/client-contracts';
const api = vi.hoisted(() => ({ listMyImportTasks: vi.fn(), listMyReportingTasks: vi.fn(), listMyAgentRuns: vi.fn() }));
vi.mock('../api/my-tasks', () => api);
const imported = (statusKey = 'preview_succeeded', extra = {}) => ({ id: 'i', requestedByUserId: 'user', tenantId: 'tenant',
  statusKey, validRowCount: 2, processedRowCount: 1, sourceFileName: 'Orders.xlsx', schemaKey: 'orders', hasErrorReceipt: false, ...extra } as ImportExportTaskResponse);
const exported = (statusKey = 'succeeded', extra = {}) => ({ id: 'r', requestedByUserId: 'user', statusKey, definitionName: 'Orders', rowCount: 100, ...extra } as ReportingExportTask);
beforeEach(() => vi.resetAllMocks());
describe('本人任务来源契约', () => {
  it('AI 来源保留等待态且只提供来源详情，不借用导入或报表写入资格', async () => {
    const run = { id: 'a', definitionKey: 'workflow', statusKey: 'running', createdAtUtc: '2026-11-05T00:00:00Z' } as AiAgentRunResponse;
    const task = agentTaskItem(run);
    expect(taskPending(task)).toBe(true);
    for (const statusKey of ['awaiting_approval', 'authorization_required', 'reconciliation_required', 'retry_scheduled', 'completed', 'failed', 'cancelled', 'expired']) {
      const item = agentTaskItem({ ...run, statusKey });
      expect(taskPending(item)).toBe(false); expect(taskActions(item, () => true)).toEqual(['detail']);
      expect(item.progress).toBeNull(); expect(item.hasResult).toBe(false);
    }
    api.listMyAgentRuns.mockResolvedValue({ items: [run], nextCursor: 'next', hasMore: true });
    const signal = new AbortController().signal;
    const result = await readMyTaskSource('agents', { userId: 'user', tenantId: 'tenant' }, 'before', 'running', signal);
    expect(api.listMyAgentRuns).toHaveBeenCalledWith('before', 'running', signal);
    expect(result.items).toEqual([task]); expect(result.nextCursor).toBe('next');
  });
  it('已知导入行数保留，报表结果行数和未知有效期不伪装为进度', () => {
    expect(importTaskItem(imported()).progress).toEqual({ processed: 1, total: 2 });
    expect(importTaskItem(imported('preview_failed', { validRowCount: 0 })).progress).toBeNull();
    expect(reportingTaskItem(exported()).progress).toBeNull();
    expect(reportingTaskItem(exported()).resultExpiresAtUtc).toBeNull();
  });
  it('操作资格保持来源状态及精确权限，取消仅在来源允许时出现', () => {
    expect(taskActions(importTaskItem(imported()), () => false)).toEqual(['detail']);
    const can = (code: string) => code === 'import_export.import_tasks.execute';
    expect(taskActions(importTaskItem(imported()), can)).toEqual(['detail', 'execute']);
    expect(taskActions(importTaskItem(imported('execution_partial', { hasErrorReceipt: true })), can)).toEqual(['detail', 'resume', 'retry', 'errors']);
    expect(taskActions(importTaskItem(imported('execution_failed')), can)).toEqual(['detail', 'retry']);
    expect(taskActions(importTaskItem(imported('executing')), can)).toEqual(['detail']);
    expect(taskActions(reportingTaskItem(exported()), code => code === 'reporting.export_tasks.download')).toEqual(['detail', 'download']);
    expect(taskActions(reportingTaskItem(exported('processing')), () => true)).toEqual(['detail']);
  });
  it('待执行取消使用独立权限，已取消任务没有恢复或再次执行', () => {
    const can = (code: string) => code === 'import_export.import_tasks.cancel';
    expect(taskActions(importTaskItem(imported('queued')), can)).toEqual(['detail', 'cancel']);
    expect(taskActions(importTaskItem(imported('executing')), can)).toEqual(['detail']);
    expect(taskActions(importTaskItem(imported('canceled')), () => true)).toEqual(['detail']);
    expect(taskPending(importTaskItem(imported('canceled')))).toBe(false);
    expect(taskActions(reportingTaskItem(exported('queued')), code => code === 'reporting.export_tasks.cancel')).toEqual(['detail', 'cancel']);
    expect(taskActions(reportingTaskItem(exported('canceled')), () => true)).toEqual(['detail']);
  });
  it('仅排队和实际执行状态需要刷新', () => {
    expect(taskPending(importTaskItem(imported('preview_succeeded')))).toBe(false);
    expect(taskPending(importTaskItem(imported('executing')))).toBe(true);
    expect(taskPending(reportingTaskItem(exported('processing')))).toBe(true);
    expect(taskPending(reportingTaskItem(exported()))).toBe(false);
  });
  it('游标和状态直接派发来源，禁止本地过滤掩盖本人分页错误', async () => {
    api.listMyImportTasks.mockResolvedValue({ items: [imported()], nextCursor: 'next', hasMore: true });
    const controller = new AbortController();
    const result = await readMyTaskSource('imports', { userId: 'user', tenantId: 'tenant' }, 'before', 'preview_succeeded', controller.signal);
    expect(api.listMyImportTasks).toHaveBeenCalledWith('before', 'preview_succeeded', controller.signal);
    expect(result.nextCursor).toBe('next'); expect(result.items[0]?.title).toBe('Orders.xlsx');
    api.listMyImportTasks.mockResolvedValue({ items: [imported(), imported('queued', { requestedByUserId: 'other' })] });
    await expect(readMyTaskSource('imports', { userId: 'user', tenantId: 'tenant' })).rejects.toThrow('owner_mismatch');
    api.listMyImportTasks.mockResolvedValue({ items: [imported('queued', { tenantId: 'other' })] });
    await expect(readMyTaskSource('imports', { userId: 'user', tenantId: 'tenant' })).rejects.toThrow('owner_mismatch');
    api.listMyReportingTasks.mockResolvedValue({ items: [exported('queued', { requestedByUserId: 'other' })] });
    await expect(readMyTaskSource('reports', { userId: 'user', tenantId: 'tenant' })).rejects.toThrow('owner_mismatch');
  });
});
