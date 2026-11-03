import {
  isReportingExportTaskDetail,
  isReportingExportTaskPage,
  type CreateReportingExportTaskRequest,
  type ReportingExportTaskDetail,
  type ReportingExportTaskPage
} from '@fullnet/client-contracts';
import { request, requestBlob } from './http';
import { http } from './http';
import { reportingCancelExportTask } from '@fullnet/client-contracts';

export async function createReportingExportTask(
  body: CreateReportingExportTaskRequest,
  signal?: AbortSignal
): Promise<ReportingExportTaskDetail> {
  const value = await request<unknown>(
    '/api/v1/reporting/export-tasks',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isReportingExportTaskDetail(value)) {
    throw new Error('client.invalid_reporting_export_task_detail');
  }
  return value;
}

export async function listReportingExportTasks(
  page = 1,
  pageSize = 20,
  definitionId?: string,
  signal?: AbortSignal
): Promise<ReportingExportTaskPage> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  if (definitionId) {
    params.set('definitionId', definitionId);
  }
  const value = await request<unknown>(
    `/api/v1/reporting/export-tasks?${params.toString()}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingExportTaskPage(value)) {
    throw new Error('client.invalid_reporting_export_task_page');
  }
  return value;
}

export async function getReportingExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ReportingExportTaskDetail> {
  const value = await request<unknown>(
    `/api/v1/reporting/export-tasks/${encodeURIComponent(taskId)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingExportTaskDetail(value)) {
    throw new Error('client.invalid_reporting_export_task_detail');
  }
  return value;
}

export async function downloadReportingExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<Blob> {
  return requestBlob(
    `/api/v1/reporting/export-tasks/${encodeURIComponent(taskId)}/download`,
    { method: 'GET' },
    signal
  );
}

/** 仅停止本人排队版本；结果必须仍指向同一任务且已经进入取消终态。 */
export async function cancelReportingExportTask(taskId: string, version: number, signal?: AbortSignal): Promise<ReportingExportTaskDetail> {
  const value = await reportingCancelExportTask(http, { taskId, body: { version } }, signal);
  if (!isReportingExportTaskDetail(value) || value.id.toLowerCase() !== taskId.toLowerCase()
    || value.statusKey !== 'canceled' || value.version < version) throw new Error('client.invalid_reporting_export_task_detail');
  return value;
}
