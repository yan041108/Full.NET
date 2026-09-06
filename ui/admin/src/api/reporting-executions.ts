import {
  isReportingExecutionPage,
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
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const value = await request<unknown>(
    `/api/v1/reporting/definitions/${encodeURIComponent(definitionId)}/execute?${params.toString()}`,
    { method: 'POST', body },
    signal
  );
  if (!isReportingExecutionPage(value)) {
    throw new Error('client.invalid_reporting_execution_page');
  }
  return value;
}
