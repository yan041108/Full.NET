import {
  importExportCreateImportTask,
  importExportDownloadImportTaskErrorReceipt,
  importExportDownloadStaticSchemaTemplate,
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

// 结构正确也可能属于另一任务；在薄适配层统一拒绝，避免详情和执行消费者接入错配结果。
function readTaskResult(value: unknown, taskId: string): ImportExportTaskDetailResponse {
  if (!isImportExportTaskDetailResponse(value)) throw new Error('client.invalid_import_export_task');
  if (value.id.toLowerCase() !== taskId.toLowerCase()) throw new Error('client.invalid_import_export_task_identity');
  return value;
}

// Unicode White_Space 与服务端键裁剪对齐：包含 NEL，不把 BOM 当作可裁剪空白。
function trimImportKey(value: string): string {
  return value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
}

/** 下载所选静态 Schema/工作表的 Excel 模板，复用正式生成客户端。 */
export async function downloadStaticImportTemplate(schemaKey: string, worksheetKey: string, signal?: AbortSignal): Promise<Blob> {
  const value = await importExportDownloadStaticSchemaTemplate(http, { schemaKey, worksheetKey }, signal);
  if (!(value instanceof Blob)) throw new Error('client.invalid_import_export_template');
  return value;
}

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
  return readTaskResult(value, taskId);
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
  // 服务端创建入口只去除首尾空白，Schema/工作表机器键仍按精确大小写匹配。
  if (value.schemaKey !== trimImportKey(schemaKey) || value.worksheetKey !== trimImportKey(worksheetKey))
    throw new Error('client.invalid_import_export_task_identity');
  return value;
}

/** 将预校验成功任务排队执行。 */
export async function executeImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportExecuteImportTask(http, { taskId }, signal);
  return readTaskResult(value, taskId);
}

/** 从部分成功检查点恢复执行。 */
export async function resumeImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportResumeImportTask(http, { taskId }, signal);
  return readTaskResult(value, taskId);
}

/** 重置并重新排队执行。 */
export async function retryImportExportTask(
  taskId: string,
  signal?: AbortSignal
): Promise<ImportExportTaskDetailResponse> {
  const value = await importExportRetryImportTask(http, { taskId }, signal);
  return readTaskResult(value, taskId);
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
