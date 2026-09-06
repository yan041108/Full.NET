import {
  importExportCreateImportTask,
  importExportGetImportTask,
  importExportListImportTasks,
  importExportListStaticSchemas,
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

export type {
  ImportExportTaskDetailResponse,
  ImportExportTaskPage,
  StaticImportSchemaDefinition
};
