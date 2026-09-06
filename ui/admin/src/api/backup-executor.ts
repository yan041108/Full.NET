import {
  isPlatformBackupExecutorStatus,
  isPlatformBackupRun,
  isPlatformBackupRunPage,
  isPlatformBackupTask,
  type PlatformBackupExecutorStatus,
  type PlatformBackupRun,
  type PlatformBackupRunListQuery,
  type PlatformBackupRunPage,
  type PlatformBackupTask
} from '@fullnet/client-contracts';
import { request, requestBlob } from './http';

function buildRunListQuery(query: PlatformBackupRunListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.taskId) {
    params.set('taskId', query.taskId);
  }
  if (query.status) {
    params.set('status', query.status);
  }
  if (query.fromUtc) {
    params.set('fromUtc', query.fromUtc);
  }
  if (query.toUtc) {
    params.set('toUtc', query.toUtc);
  }
  return params.toString();
}

/** 读取授权备份执行器部署状态。 */
export async function getBackupExecutorStatus(
  signal?: AbortSignal
): Promise<PlatformBackupExecutorStatus> {
  const value = await request<unknown>(
    '/api/v1/platform/backup-executor/status',
    { method: 'GET' },
    signal
  );
  if (!isPlatformBackupExecutorStatus(value)) {
    throw new Error('client.invalid_backup_executor_status');
  }

  return value;
}

/** 列出授权备份任务目录。 */
export async function listBackupTasks(
  signal?: AbortSignal
): Promise<PlatformBackupTask[]> {
  const value = await request<unknown>(
    '/api/v1/platform/backup-executor/tasks',
    { method: 'GET' },
    signal
  );
  if (!Array.isArray(value) || !value.every(isPlatformBackupTask)) {
    throw new Error('client.invalid_backup_tasks');
  }

  return value;
}

/** 按标识查询授权备份任务。 */
export async function getBackupTask(
  taskId: string,
  signal?: AbortSignal
): Promise<PlatformBackupTask> {
  const value = await request<unknown>(
    `/api/v1/platform/backup-executor/tasks/${taskId}`,
    { method: 'GET' },
    signal
  );
  if (!isPlatformBackupTask(value)) {
    throw new Error('client.invalid_backup_task');
  }

  return value;
}

/** 分页查询授权备份运行结果。 */
export async function listBackupRuns(
  query: PlatformBackupRunListQuery = {},
  signal?: AbortSignal
): Promise<PlatformBackupRunPage> {
  const value = await request<unknown>(
    `/api/v1/platform/backup-executor/runs?${buildRunListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isPlatformBackupRunPage(value)) {
    throw new Error('client.invalid_backup_runs');
  }

  return value;
}

/** 按标识查询授权备份运行结果。 */
export async function getBackupRun(
  runId: string,
  signal?: AbortSignal
): Promise<PlatformBackupRun> {
  const value = await request<unknown>(
    `/api/v1/platform/backup-executor/runs/${runId}`,
    { method: 'GET' },
    signal
  );
  if (!isPlatformBackupRun(value)) {
    throw new Error('client.invalid_backup_run');
  }

  return value;
}

/** 受控下载授权备份产物。 */
export async function downloadBackupRunArtifact(
  runId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return requestBlob(
    `/api/v1/platform/backup-executor/runs/${runId}/download`,
    { method: 'GET' },
    signal
  );
}

export type {
  PlatformBackupExecutorStatus,
  PlatformBackupRun,
  PlatformBackupRunListQuery,
  PlatformBackupRunPage,
  PlatformBackupTask
};
