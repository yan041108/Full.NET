export interface ReportingDataSourceListItem {
  id: string;
  tenantId: string | null;
  name: string;
  providerKey: string;
  maskedServerEndpoint: string;
  maskedDatabaseName: string;
  maskedUsername: string;
  hasPassword: boolean;
  trustServerCertificate: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface ReportingDataSource {
  id: string;
  tenantId: string | null;
  name: string;
  providerKey: string;
  serverHost: string;
  port: number;
  databaseName: string;
  username: string;
  hasPassword: boolean;
  trustServerCertificate: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface ReportingDataSourcePage {
  items: ReportingDataSourceListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateReportingDataSourceRequest {
  tenantId?: string | null;
  name: string;
  providerKey: string;
  serverHost: string;
  port: number;
  databaseName: string;
  username: string;
  password: string;
  trustServerCertificate: boolean;
  isEnabled: boolean;
}

export interface UpdateReportingDataSourceRequest {
  name: string;
  providerKey: string;
  serverHost: string;
  port: number;
  databaseName: string;
  username: string;
  password?: string | null;
  trustServerCertificate: boolean;
  isEnabled: boolean;
  version: number;
}

export interface TestReportingDataSourceResult {
  succeeded: boolean;
  message: string;
}

export interface ReportingDataSourceListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  nameContains?: string;
  isEnabled?: boolean;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isReportingDataSourceListItem(value: unknown): value is ReportingDataSourceListItem {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.providerKey === 'string'
    && typeof value.maskedServerEndpoint === 'string'
    && typeof value.maskedDatabaseName === 'string'
    && typeof value.maskedUsername === 'string'
    && typeof value.hasPassword === 'boolean'
    && typeof value.trustServerCertificate === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && (value.lastTestedAtUtc === null || typeof value.lastTestedAtUtc === 'string')
    && isNullableString(value.lastTestStatusKey)
    && isNullableString(value.lastTestMessage)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isReportingDataSource(value: unknown): value is ReportingDataSource {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.providerKey === 'string'
    && typeof value.serverHost === 'string'
    && typeof value.port === 'number'
    && typeof value.databaseName === 'string'
    && typeof value.username === 'string'
    && typeof value.hasPassword === 'boolean'
    && typeof value.trustServerCertificate === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && (value.lastTestedAtUtc === null || typeof value.lastTestedAtUtc === 'string')
    && isNullableString(value.lastTestStatusKey)
    && isNullableString(value.lastTestMessage)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isReportingDataSourcePage(value: unknown): value is ReportingDataSourcePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isReportingDataSourceListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isTestReportingDataSourceResult(value: unknown): value is TestReportingDataSourceResult {
  return isRecord(value)
    && typeof value.succeeded === 'boolean'
    && typeof value.message === 'string';
}
