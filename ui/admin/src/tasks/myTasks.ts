import type { CursorPage, ImportExportTaskResponse, ReportingExportTask } from '@fullnet/client-contracts';
import { listMyImportTasks, listMyReportingTasks } from '../api/my-tasks';

export type MyTaskSource = 'imports' | 'reports';
export type MyTaskAction = 'detail' | 'execute' | 'resume' | 'retry' | 'errors' | 'download' | 'cancel';
export interface MyTaskItem {
  id: string;
  source: MyTaskSource;
  title: string;
  businessType: string;
  statusKey: string;
  createdAtUtc: string;
  progress: { processed: number; total: number } | null;
  resultExpiresAtUtc: null;
  hasResult: boolean;
}
export const sourceReadPermissions = new Map<MyTaskSource, string>([
  ['imports', 'import_export.import_tasks.read'], ['reports', 'reporting.export_tasks.read']
]);
export const sourceStatusKeys = new Map<MyTaskSource, readonly string[]>([
  ['imports', ['uploaded', 'preview_succeeded', 'preview_failed', 'queued', 'executing', 'execution_succeeded', 'execution_partial', 'execution_failed', 'canceled']],
  ['reports', ['queued', 'processing', 'succeeded', 'failed', 'canceled']]
]);
export const sourceDetailRoutes = new Map<MyTaskSource, string>([['imports', 'import-export-tasks'], ['reports', 'reporting-export-tasks']]);

/** 保留来源自己的状态与已知数量，不把预校验或结果行数伪装成执行进度。 */
export function importTaskItem(task: ImportExportTaskResponse): MyTaskItem {
  return { id: task.id, source: 'imports', title: task.sourceFileName ?? task.schemaDisplayName, businessType: task.schemaKey,
    statusKey: task.statusKey, createdAtUtc: task.createdAtUtc,
    progress: task.validRowCount > 0 ? { processed: task.processedRowCount, total: task.validRowCount } : null,
    resultExpiresAtUtc: null, hasResult: task.hasErrorReceipt };
}
export function reportingTaskItem(task: ReportingExportTask): MyTaskItem {
  return { id: task.id, source: 'reports', title: task.definitionName, businessType: task.definitionKey,
    statusKey: task.statusKey, createdAtUtc: task.createdAtUtc, progress: null, resultExpiresAtUtc: null,
    hasResult: task.statusKey === 'succeeded' };
}
export function taskPending(task: MyTaskItem): boolean {
  return task.statusKey === 'queued' || task.source === 'imports' && task.statusKey === 'executing'
    || task.source === 'reports' && task.statusKey === 'processing';
}
export function taskActions(task: MyTaskItem, can: (permission: string) => boolean): MyTaskAction[] {
  const actions: MyTaskAction[] = ['detail'];
  if (task.source === 'reports') {
    if (task.hasResult && can('reporting.export_tasks.download')) actions.push('download');
    if (task.statusKey === 'queued' && can('reporting.export_tasks.cancel')) actions.push('cancel');
  } else if (can('import_export.import_tasks.execute')) {
    if (task.statusKey === 'preview_succeeded' && task.progress?.total) actions.push('execute');
    if (task.statusKey === 'execution_partial') actions.push('resume');
    if (['execution_partial', 'execution_failed'].includes(task.statusKey)) actions.push('retry');
    if (task.hasResult) actions.push('errors');
  }
  if (task.source === 'imports' && ['preview_succeeded', 'queued', 'execution_partial'].includes(task.statusKey)
    && can('import_export.import_tasks.cancel')) actions.push('cancel');
  return actions;
}

/** 服务端必须先按本人分页；出现其他主体时整页失败，不做会掩盖分页错误的客户端过滤。 */
export async function readMyTaskSource(source: MyTaskSource, actor: { userId: string; tenantId: string }, cursor?: string,
  statusKey?: string, signal?: AbortSignal): Promise<CursorPage<MyTaskItem>> {
  if (source === 'imports') {
    const page = await listMyImportTasks(cursor, statusKey, signal);
    if (page.items.some(task => task.requestedByUserId !== actor.userId || task.tenantId !== actor.tenantId)) throw new Error('client.task_owner_mismatch');
    return { ...page, items: page.items.map(importTaskItem) };
  }
  const page = await listMyReportingTasks(cursor, statusKey, signal);
  if (page.items.some(task => task.requestedByUserId !== actor.userId)) throw new Error('client.task_owner_mismatch');
  return { ...page, items: page.items.map(reportingTaskItem) };
}
