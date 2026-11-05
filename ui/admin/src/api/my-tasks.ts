import { isCursorPage, isImportExportTaskResponse, isReportingExportTask, isAiAgentRunResponse,
  importExportListMyImportTasks, reportingListMyExportTasks, aiListMyAgentRuns,
  type AiAgentRunResponse, type CursorPage, type ImportExportTaskResponse, type ReportingExportTask } from '@fullnet/client-contracts';
import { http } from './http';

/** 身份来自服务器，查询不接受用户或租户覆盖参数。 */
export async function listMyImportTasks(cursor?: string, statusKey?: string, signal?: AbortSignal): Promise<CursorPage<ImportExportTaskResponse>> {
  const value = await importExportListMyImportTasks(http, { pageSize: 20, cursor, statusKey }, signal);
  if (!isCursorPage(value, isImportExportTaskResponse)) throw new Error('client.invalid_my_import_tasks');
  return value;
}
export async function listMyReportingTasks(cursor?: string, statusKey?: string, signal?: AbortSignal): Promise<CursorPage<ReportingExportTask>> {
  const value = await reportingListMyExportTasks(http, { pageSize: 20, cursor, statusKey }, signal);
  if (!isCursorPage(value, isReportingExportTask)) throw new Error('client.invalid_my_reporting_tasks');
  return value;
}
/** 服务端仅返回当前租户本人摘要；列表不触发模型调用或事件流。 */
export async function listMyAgentRuns(cursor?: string, statusKey?: string, signal?: AbortSignal): Promise<CursorPage<AiAgentRunResponse>> {
  const value = await aiListMyAgentRuns(http, { pageSize: 20, cursor, statusKey }, signal);
  if (!isCursorPage(value, isAiAgentRunResponse)) throw new Error('client.invalid_my_agent_runs');
  return value;
}
