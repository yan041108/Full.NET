import {
  isReportingDataSource,
  isReportingDataSourcePage,
  isTestReportingDataSourceResult,
  isGuid,
  type CreateReportingDataSourceRequest,
  type ReportingDataSource,
  type ReportingDataSourceListQuery,
  type ReportingDataSourcePage,
  type TestReportingDataSourceResult,
  type UpdateReportingDataSourceRequest
} from '@fullnet/client-contracts';
import { request } from './http';

const sameId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
const validId = (id: string) => isGuid(id) && id !== '00000000-0000-0000-0000-000000000000';
const validVersion = (version: number) => Number.isSafeInteger(version) && version > 0;
function validDetail(value: unknown): value is ReportingDataSource {
  return isReportingDataSource(value) && validId(value.id) && (value.tenantId === null || validId(value.tenantId))
    && validVersion(value.version) && !Object.hasOwn(value, 'password') && !Object.hasOwn(value, 'passwordProtected');
}
function echoes(value: ReportingDataSource, input: Omit<CreateReportingDataSourceRequest, 'tenantId' | 'password'>): boolean {
  return value.name === input.name.trim() && value.providerKey === input.providerKey.trim() && value.serverHost === input.serverHost.trim()
    && value.port === input.port && value.databaseName === input.databaseName.trim() && value.username === input.username.trim()
    && value.trustServerCertificate === input.trustServerCertificate && value.isEnabled === input.isEnabled;
}

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
  // 与服务端相同的分页归一化，同时冻结调用方在途可能改写的筛选值。
  const input = { ...query, page: Math.max(1, query.page ?? 1), pageSize: Math.min(100, Math.max(1, query.pageSize ?? 20)),
    tenantId: query.tenantId?.trim() || undefined, nameContains: query.nameContains?.trim() || undefined };
  if (!Number.isSafeInteger(input.page) || !Number.isSafeInteger(input.pageSize)) throw new Error('client.invalid_reporting_data_source_query');
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources?${buildListQuery(input)}`,
    { method: 'GET' },
    signal
  );
  if (!isReportingDataSourcePage(value) || value.page !== input.page || value.pageSize !== input.pageSize
    || !Number.isSafeInteger(value.total) || value.total < 0 || value.items.length > input.pageSize
    || value.items.some(row => !validId(row.id) || !validVersion(row.version) || row.tenantId !== null && !validId(row.tenantId)
      || input.tenantId !== undefined && !sameId(row.tenantId, input.tenantId) || input.isEnabled !== undefined && row.isEnabled !== input.isEnabled
      || ['serverHost', 'port', 'databaseName', 'username', 'password', 'passwordProtected'].some(key => Object.hasOwn(row, key)))
    || new Set(value.items.map(row => row.id.toLowerCase())).size !== value.items.length) {
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
  if (!validDetail(value) || !sameId(value.id, id)) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function createReportingDataSource(
  body: CreateReportingDataSourceRequest,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const input = { ...body };
  const value = await request<unknown>(
    '/api/v1/reporting/data-sources',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) },
    signal
  );
  if (!validDetail(value) || value.version !== 1 || !sameId(value.tenantId, input.tenantId) || !echoes(value, input) || !value.hasPassword) {
    throw new Error('client.invalid_reporting_data_source');
  }
  return value;
}

export async function updateReportingDataSource(
  id: string,
  body: UpdateReportingDataSourceRequest,
  signal?: AbortSignal
): Promise<ReportingDataSource> {
  const input = { ...body };
  const value = await request<unknown>(
    `/api/v1/reporting/data-sources/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) },
    signal
  );
  if (!validDetail(value) || !sameId(value.id, id) || value.version !== input.version + 1 || !echoes(value, input) || !value.hasPassword) {
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
  if (!validDetail(value) || !sameId(value.id, id) || value.isEnabled) {
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
  if (typeof value !== 'boolean') throw new Error('client.invalid_reporting_data_source_delete_result');
  return value;
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
