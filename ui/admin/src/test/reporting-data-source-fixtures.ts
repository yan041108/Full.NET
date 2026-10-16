import type { ReportingDataSource, ReportingDataSourceListItem } from '@fullnet/client-contracts';

export const dataSource: ReportingDataSource = {
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf296', tenantId: null, name: '数据源', providerKey: 'sql_server',
  serverHost: 'fixture-db', port: 1433, databaseName: 'fixture-db-name', username: 'fixture-user', hasPassword: true,
  trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null, lastTestMessage: null,
  createdAtUtc: '2026-10-16T00:00:00Z', updatedAtUtc: null, version: 1
};
export const dataSourceRow: ReportingDataSourceListItem = {
  id: dataSource.id, tenantId: null, name: dataSource.name, providerKey: dataSource.providerKey,
  maskedServerEndpoint: 'fixture-***', maskedDatabaseName: 'fixture-***', maskedUsername: 'fixture-***',
  hasPassword: true, trustServerCertificate: false, isEnabled: true, lastTestedAtUtc: null, lastTestStatusKey: null,
  lastTestMessage: null, createdAtUtc: dataSource.createdAtUtc, updatedAtUtc: null, version: 1
};
