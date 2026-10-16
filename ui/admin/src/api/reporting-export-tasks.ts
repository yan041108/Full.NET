import {
  reportingCancelExportTask,
  isGuid,
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
  if (!Array.isArray(body.parameters) || !body.parameters.every(parameter => parameter && typeof parameter.parameterKey === 'string' && !!parameter.parameterKey.trim()
    && (parameter.value === null || typeof parameter.value === 'string'))) throw new Error('client.invalid_create_reporting_export_task');
  const snapshot = { ...body, parameters: body.parameters.map(parameter => ({ ...parameter })) };
  if (!isGuid(snapshot.definitionId) || snapshot.definitionId === '00000000-0000-0000-0000-000000000000'
    || snapshot.versionNumber != null && (!Number.isSafeInteger(snapshot.versionNumber) || snapshot.versionNumber < 1)
    || new Set(snapshot.parameters.map(parameter => parameter.parameterKey)).size !== snapshot.parameters.length) throw new Error('client.invalid_create_reporting_export_task');
  const value = await request<unknown>(
    '/api/v1/reporting/export-tasks',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(snapshot) },
    signal
  );
  if (!isReportingExportTaskDetail(value)) {
    throw new Error('client.invalid_reporting_export_task_detail');
  }
  const parameters = (items: typeof snapshot.parameters) => JSON.stringify(items.map(item => JSON.stringify([item.parameterKey, item.value])).sort());
  if (value.id === '00000000-0000-0000-0000-000000000000' || value.definitionId.toLowerCase() !== snapshot.definitionId.toLowerCase()
    || snapshot.versionNumber != null && value.versionNumber !== snapshot.versionNumber || value.formatKey !== snapshot.formatKey
    || parameters(value.parameters) !== parameters(snapshot.parameters)) throw new Error('client.invalid_reporting_export_task_identity');
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
  if (!isReportingExportTaskPage(value) || value.page !== page || value.pageSize !== pageSize || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > pageSize
    || new Set(value.items.map(row => row.id.toLowerCase())).size !== value.items.length
    || value.items.some(row => row.id === '00000000-0000-0000-0000-000000000000' || definitionId !== undefined && row.definitionId.toLowerCase() !== definitionId.toLowerCase())) {
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
  if (!isReportingExportTaskDetail(value) || value.id.toLowerCase() !== taskId.toLowerCase()) {
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
