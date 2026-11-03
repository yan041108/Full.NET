import {
  reportingExecuteDefinition, reportingGetDefinitionVersion,
  type ExecuteReportingDefinitionRequest,
  type ReportingExecutionPage, type ReportingDefinitionVersion
} from '@fullnet/client-contracts';
import { http } from './http';

/** 执行页只读取明确发布版本，不使用正在编辑的草稿参数。 */
export async function getReportingExecutionVersion(definitionId: string, versionNumber: number, signal?: AbortSignal): Promise<ReportingDefinitionVersion> {
  const value = await reportingGetDefinitionVersion(http, { definitionId, versionNumber }, signal);
  if (value.definitionId.toLowerCase() !== definitionId.toLowerCase() || value.versionNumber !== versionNumber) throw new Error('client.invalid_reporting_definition_version');
  return value;
}

export async function executeReportingDefinition(
  definitionId: string,
  body: ExecuteReportingDefinitionRequest,
  page = 1,
  pageSize = 50,
  signal?: AbortSignal
): Promise<ReportingExecutionPage> {
  const value = await reportingExecuteDefinition(http, { definitionId, page, pageSize, body: { ...body, versionNumber: body.versionNumber ?? null } }, signal);
  if (value.definitionId.toLowerCase() !== definitionId.toLowerCase() || value.page !== page || value.pageSize !== pageSize
    || (body.versionNumber != null && value.versionNumber !== body.versionNumber)
    || value.rows.some(row => Object.values(row.values).some(cell => cell !== null && typeof cell !== 'string'))) {
    throw new Error('client.invalid_reporting_execution_page');
  }
  // 生成字典保持 unknown；上面的逐单元校验已收窄为执行页契约。
  return value as ReportingExecutionPage;
}
