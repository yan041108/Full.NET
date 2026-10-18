import type { ReportingDefinitionVersion, ReportingGroup, ReportingQueryPortDefinition } from '@fullnet/client-contracts';
import { reportDefinition, outputId } from './data-output-fixtures';
import { dataSource } from './reporting-data-source-fixtures';
export const reportingGroup: ReportingGroup = { id: outputId, parentId: null, name: '报表组', sortOrder: 0,
  isEnabled: true, createdAtUtc: reportDefinition.createdAtUtc, updatedAtUtc: null, version: 1 };
export const reportingParameter = { parameterKey: 'year', displayName: '年度', dataTypeKey: 'string', isRequired: true, defaultValue: '2026' };
export const reportingDefinition = { ...reportDefinition, dataSourceId: dataSource.id, parameterSchema: [{ ...reportingParameter }] };
export const reportingPort: ReportingQueryPortDefinition = { queryPortKey: reportingDefinition.queryPortKey, displayName: '目录', description: '',
  supportedProviderKeys: ['sql_server'], parameters: [{ ...reportingParameter, minimum: null, maximum: null }] };
export const reportingVersion: ReportingDefinitionVersion = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  definitionId: reportingDefinition.id, versionNumber: 2, dataSourceId: dataSource.id, queryPortKey: reportingDefinition.queryPortKey,
  parameterSchema: [{ ...reportingParameter }], layoutConfigJson: '{}', changeNote: null, publishedByUserId: outputId, publishedAtUtc: reportDefinition.createdAtUtc };
export const reportingInitialVersion: ReportingDefinitionVersion = { ...reportingVersion, id: '019bc2b1-2a40-7cc3-8992-a80de51bf293', versionNumber: 1 };
