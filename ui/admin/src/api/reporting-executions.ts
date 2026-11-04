import {
  reportingExecuteDefinition, reportingGetDefinitionVersion,
  isGuid,
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
  // 请求与回包核对共用副本，不允许调用方在等待期间改变原始查询身份。
  if (!isGuid(definitionId) || definitionId === '00000000-0000-0000-0000-000000000000'
    || !Number.isSafeInteger(page) || page < 1 || page > 2147483647 || !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 200
    || body.versionNumber != null && (!Number.isSafeInteger(body.versionNumber) || body.versionNumber < 1 || body.versionNumber > 2147483647)
    || !Array.isArray(body.parameters) || body.parameters.some(parameter => !parameter || typeof parameter.parameterKey !== 'string' || !parameter.parameterKey.trim() || parameter.value !== null && typeof parameter.value !== 'string')
    || new Set(body.parameters.map(parameter => parameter.parameterKey)).size !== body.parameters.length) throw new Error('client.invalid_reporting_execution_request');
  const payload = { ...body, parameters: body.parameters.map(parameter => ({ ...parameter })) };
  const value = await reportingExecuteDefinition(http, { definitionId, page, pageSize, body: { ...payload, versionNumber: payload.versionNumber ?? null } }, signal);
  if (value.definitionId.toLowerCase() !== definitionId.toLowerCase() || value.rows.length > pageSize || value.page !== page || value.pageSize !== pageSize
    || (payload.versionNumber != null && value.versionNumber !== payload.versionNumber)
    || value.rows.some(row => Object.values(row.values).some(cell => cell !== null && typeof cell !== 'string'))) {
    throw new Error('client.invalid_reporting_execution_page');
  }
  // 生成字典保持 unknown；上面的逐单元校验已收窄为执行页契约。
  return value as ReportingExecutionPage;
}
