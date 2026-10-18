import {
  isReportingExecutionPage,
  isGuid,
  type ExecuteReportingDefinitionRequest,
  type ReportingExecutionPage
} from '@fullnet/client-contracts';
import { request } from './http';

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
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(definitionId)}/execute?${params.toString()}`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!isReportingExecutionPage(value) || value.definitionId.toLowerCase() !== definitionId.toLowerCase()
    || !Number.isSafeInteger(value.versionNumber) || value.versionNumber < 1 || payload.versionNumber != null && value.versionNumber !== payload.versionNumber
    || value.page !== page || value.pageSize !== pageSize || value.rows.length > pageSize
    || value.totalRows !== null && (!Number.isSafeInteger(value.totalRows) || value.totalRows < 0)
    || value.columns.some(column => !column.columnKey.trim()) || new Set(value.columns.map(column => column.columnKey)).size !== value.columns.length
    || value.rows.some(row => Object.values(row.values).some(cell => cell !== null && typeof cell !== 'string'))) {
    throw new Error('client.invalid_reporting_execution_page');
  }
  return value;
}
