import {
  reportingCancelExportTask,
  isReportingExportTaskDetail,
  isReportingExportTaskPage,
  type CreateReportingExportTaskRequest,
  type ReportingExportTaskDetail,
  type ReportingExportTaskPage
} from '@fullnet/client-contracts';
import { http, request, requestBlob } from './http';

// 取消结果必须属于请求任务，不能把其他任务的终态接入当前页面。
export async function cancelReportingExportTask(taskId: string, signal?: AbortSignal): Promise<ReportingExportTaskDetail> {
  const result = await reportingCancelExportTask(http, { taskId }, signal);
  if (!isReportingExportTaskDetail(result) || result.id.toLowerCase() !== taskId.toLowerCase() || result.statusKey !== 'cancelled')
    throw new Error('client.invalid_reporting_export_task_cancellation');
  return result;
}

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
