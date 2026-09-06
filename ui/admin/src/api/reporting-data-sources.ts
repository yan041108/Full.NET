import {
  isReportingDataSource,
  isReportingDataSourcePage,
  isTestReportingDataSourceResult,
  type CreateReportingDataSourceRequest,
  type ReportingDataSource,
  type ReportingDataSourceListQuery,
  type ReportingDataSourcePage,
  type TestReportingDataSourceResult,
  type UpdateReportingDataSourceRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: ReportingDataSourceListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId?.trim()) {
    params.set('tenantId', query.tenantId.trim());
  }
  if (query.nameContains?.trim()) {
    params.set('nameContains', query.nameContains.trim());
  }
  if (query.isEnabled !== undefined) {
    params.set('isEnabled', String(query.isEnabled));
  }
  return params.toString();
}

export async function listReportingDataSources(
  query: ReportingDataSourceListQuery = {},
  signal?: AbortSignal
): Promise<ReportingDataSourcePage> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDataSourcePage(value)) {
    throw new Error('client.invalid_reporting_data_source_page');
  }
  return value;
}

export async function getReportingDataSource(
  id: string,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDataSource(value)) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function createReportingDataSource(
  body: CreateReportingDataSourceRequest,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const value = await request<unknown>(
    '/api/v1/reporting/data-sources',
    { method: 'POST', body },
    signal
  );
  if (!isReportingDataSource(value)) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function updateReportingDataSource(
  id: string,
  body: UpdateReportingDataSourceRequest,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isReportingDataSource(value)) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function disableReportingDataSource(
  id: string,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}/disable`,
    { method: 'POST' },
    signal
  );
  if (!isReportingDataSource(value)) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function deleteReportingDataSource(
  id: string,
  signal?: AbortSignal
): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
  return value === true;
}

export async function testReportingDataSource(
  id: string,
  signal?: AbortSignal
): Promise<TestReportingDataSourceResult> {
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}/test`,
    { method: 'POST' },
    signal
  );
  if (!isTestReportingDataSourceResult(value)) {
    throw new Error('client.invalid_reporting_data_source_test_result');
  }
  return value;
}
