import {
  importExportCreateImportTask,
  importExportDownloadImportTaskErrorReceipt,
  importExportExecuteImportTask,
  importExportGetImportTask,
  importExportListImportTasks,
  importExportListStaticSchemas,
  importExportResumeImportTask,
  importExportRetryImportTask,
  isImportExportTaskDetailResponse,
  isImportExportTaskPage,
  isStaticImportSchemaDefinition,
  type ImportExportTaskDetailResponse,
  type ImportExportTaskPage,
  type StaticImportSchemaDefinition
} from '@fullnet/client-contracts';
import { http } from './http';

/** 列出已注册的静态导入 Schema。 */
export async function listStaticImportSchemas(
  signal?: AbortSignal
): Promise<StaticImportSchemaDefinition[]> {
  const value = await importExportListStaticSchemas(http, {}, signal);
  if (!Array.isArray(value) || !value.every(isStaticImportSchemaDefinition)) {
    throw new Error('client.invalid_static_import_schema_list');
  }
  return value;
}

/** 分页查询导入任务。 */
export async function listImportExportTasks(
  page = 1,
  pageSize = 20,
  schemaKey?: string,
  signal?: AbortSignal
): Promise<ImportExportTaskPage> {
  const value = await importExportListImportTasks(
    http,
    { page, pageSize, schemaKey },
    signal
  );
  if (!isImportExportTaskPage(value)) {
    throw new Error('client.invalid_import_export_task_page');
  }
  return value;
}

/** 读取导入任务详情。 */
export async function getImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportGetImportTask(http, { taskId }, signal);
  if (!isImportExportTaskDetailResponse(value)) {
    throw new Error('client.invalid_import_export_task');
  }
  return value;
}

/** 上传工作簿并创建导入预校验任务。 */
export async function createImportExportTask(
  schemaKey: string,
  worksheetKey: string,
  file: File,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportCreateImportTask(
    http,
    { schemaKey, worksheetKey, file },
    signal
  );
  if (!isImportExportTaskDetailResponse(value)) {
    throw new Error('client.invalid_import_export_task');
  }
  return value;
}

/** 将预校验成功任务排队执行。 */
export async function executeImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportExecuteImportTask(http, { taskId }, signal);
  if (!isImportExportTaskDetailResponse(value)) {
    throw new Error('client.invalid_import_export_task');
  }
  return value;
}

/** 从部分成功检查点恢复执行。 */
export async function resumeImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportResumeImportTask(http, { taskId }, signal);
  if (!isImportExportTaskDetailResponse(value)) {
    throw new Error('client.invalid_import_export_task');
  }
  return value;
}

/** 重置并重新排队执行。 */
export async function retryImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportRetryImportTask(http, { taskId }, signal);
  if (!isImportExportTaskDetailResponse(value)) {
    throw new Error('client.invalid_import_export_task');
  }
  return value;
}

/** 下载错误回执 xlsx。 */
export async function downloadImportExportTaskErrorReceipt(
  taskId: string,
  signal?: AbortSignal
): Promise<Blob> {
  const response = await importExportDownloadImportTaskErrorReceipt(http, { taskId }, signal);
  if (!(response instanceof Blob)) {
    throw new Error('client.invalid_import_export_error_receipt');
  }
  return response;
}

export type {
  ImportExportTaskDetailResponse,
  ImportExportTaskPage,
  StaticImportSchemaDefinition
};
