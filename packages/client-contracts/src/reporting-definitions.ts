export interface ReportingGroup {
  id: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface ReportingParameterSchemaEntry {
  parameterKey: string;
  displayName: string;
  dataTypeKey: string;
  isRequired: boolean;
  defaultValue: string | null;
}

export interface ReportingQueryPortParameterDefinition {
  parameterKey: string;
  displayName: string;
  dataTypeKey: string;
  isRequired: boolean;
  defaultValue: string | null;
  minimum: number | null;
  maximum: number | null;
}

export interface ReportingQueryPortDefinition {
  queryPortKey: string;
  displayName: string;
  description: string;
  supportedProviderKeys: string[];
  parameters: ReportingQueryPortParameterDefinition[];
}

export interface ReportingDefinition {
  id: string;
  groupId: string;
  dataSourceId: string;
  definitionKey: string;
  name: string;
  description: string | null;
  queryPortKey: string;
  parameterSchema: ReportingParameterSchemaEntry[];
  layoutConfigJson: string;
  latestPublishedVersionNumber: number;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface ReportingDefinitionVersion {
  id: string;
  definitionId: string;
  versionNumber: number;
  dataSourceId: string;
  queryPortKey: string;
  parameterSchema: ReportingParameterSchemaEntry[];
  layoutConfigJson: string;
  changeNote: string | null;
  publishedByUserId: string;
  publishedAtUtc: string;
}

export interface CreateReportingGroupRequest {
  parentId?: string | null;
  name: string;
  sortOrder: number;
  isEnabled: boolean;
}

export interface UpdateReportingGroupRequest {
  parentId?: string | null;
  name: string;
  sortOrder: number;
  isEnabled: boolean;
  version: number;
}

export interface CreateReportingDefinitionRequest {
  groupId: string;
  dataSourceId: string;
  definitionKey: string;
  name: string;
  description?: string | null;
  queryPortKey: string;
  parameterSchema: ReportingParameterSchemaEntry[];
  layoutConfigJson?: string | null;
  isEnabled: boolean;
}

export interface UpdateReportingDefinitionRequest {
  groupId: string;
  dataSourceId: string;
  name: string;
  description?: string | null;
  queryPortKey: string;
  parameterSchema: ReportingParameterSchemaEntry[];
  layoutConfigJson?: string | null;
  isEnabled: boolean;
  version: number;
}

export interface PublishReportingDefinitionRequest {
  changeNote?: string | null;
  version: number;
}

export interface ReportingDefinitionListQuery {
  groupId?: string;
  nameContains?: string;
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

export function isReportingParameterSchemaEntry(value: unknown): value is ReportingParameterSchemaEntry {
  return isRecord(value)
    && typeof value.parameterKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.dataTypeKey === 'string'
    && typeof value.isRequired === 'boolean'
    && isNullableString(value.defaultValue);
}

export function isReportingGroup(value: unknown): value is ReportingGroup {
  return isRecord(value)
    && isGuid(value.id)
    && (value.parentId === null || isGuid(value.parentId))
    && typeof value.name === 'string'
    && typeof value.sortOrder === 'number'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isReportingQueryPortDefinition(value: unknown): value is ReportingQueryPortDefinition {
  return isRecord(value)
    && typeof value.queryPortKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.description === 'string'
    && Array.isArray(value.supportedProviderKeys)
    && value.supportedProviderKeys.every(item => typeof item === 'string')
    && Array.isArray(value.parameters)
    && value.parameters.every(isReportingQueryPortParameterDefinition);
}

function isReportingQueryPortParameterDefinition(value: unknown): value is ReportingQueryPortParameterDefinition {
  return isRecord(value)
    && typeof value.parameterKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.dataTypeKey === 'string'
    && typeof value.isRequired === 'boolean'
    && isNullableString(value.defaultValue)
    && (value.minimum === null || typeof value.minimum === 'number')
    && (value.maximum === null || typeof value.maximum === 'number');
}

export function isReportingDefinition(value: unknown): value is ReportingDefinition {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.groupId)
    && isGuid(value.dataSourceId)
    && typeof value.definitionKey === 'string'
    && typeof value.name === 'string'
    && isNullableString(value.description)
    && typeof value.queryPortKey === 'string'
    && Array.isArray(value.parameterSchema)
    && value.parameterSchema.every(isReportingParameterSchemaEntry)
    && typeof value.layoutConfigJson === 'string'
    && typeof value.latestPublishedVersionNumber === 'number'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isReportingDefinitionVersion(value: unknown): value is ReportingDefinitionVersion {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.definitionId)
    && typeof value.versionNumber === 'number'
    && isGuid(value.dataSourceId)
    && typeof value.queryPortKey === 'string'
    && Array.isArray(value.parameterSchema)
    && value.parameterSchema.every(isReportingParameterSchemaEntry)
    && typeof value.layoutConfigJson === 'string'
    && isNullableString(value.changeNote)
    && isGuid(value.publishedByUserId)
    && typeof value.publishedAtUtc === 'string';
}

export function isReportingGroupList(value: unknown): value is ReportingGroup[] {
  return Array.isArray(value) && value.every(isReportingGroup);
}

export function isReportingDefinitionList(value: unknown): value is ReportingDefinition[] {
  return Array.isArray(value) && value.every(isReportingDefinition);
}

export function isReportingDefinitionVersionList(value: unknown): value is ReportingDefinitionVersion[] {
  return Array.isArray(value) && value.every(isReportingDefinitionVersion);
}

export function isReportingQueryPortList(value: unknown): value is ReportingQueryPortDefinition[] {
  return Array.isArray(value) && value.every(isReportingQueryPortDefinition);
}
