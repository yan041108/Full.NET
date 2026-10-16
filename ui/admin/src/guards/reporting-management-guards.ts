import { isGuid, isReportingDefinition, isReportingDefinitionVersion, isReportingGroup, isReportingQueryPortDefinition,
  type ReportingDefinition, type ReportingDefinitionVersion, type ReportingGroup, type ReportingParameterSchemaEntry,
  type ReportingQueryPortDefinition, type UpdateReportingDefinitionRequest } from '@fullnet/client-contracts';

export const reportingId = (value: string) => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
export const reportingRevision = (value: number) => Number.isSafeInteger(value) && value > 0;
export const sameReportingId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
function uniqueKeys(values: { parameterKey: string }[]): boolean { return values.every(row => row.parameterKey.trim().length > 0) && new Set(values.map(row => row.parameterKey)).size === values.length; }
export function sameReportingParameters(left: ReportingParameterSchemaEntry[], right: ReportingParameterSchemaEntry[]): boolean {
  const key = (rows: ReportingParameterSchemaEntry[]) => JSON.stringify(rows.map(row => [row.parameterKey, row.displayName, row.dataTypeKey, row.isRequired, row.defaultValue]));
  return key(left) === key(right);
}
function layoutKey(text: string | null | undefined): string {
  const value: unknown = JSON.parse(text?.trim() || '{}');
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw Error('client.invalid_reporting_layout');
  const canonical = (node: unknown): unknown => Array.isArray(node) ? ['array', node.map(canonical)]
    : node !== null && typeof node === 'object' ? ['object', Object.entries(node).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([key, entry]) => [key, canonical(entry)])] : node;
  return JSON.stringify(canonical(value));
}
export function sameReportingLayout(left: string | null | undefined, right: string | null | undefined): boolean {
  try { return layoutKey(left) === layoutKey(right); } catch { return false; }
}
export function validReportingGroup(value: unknown): value is ReportingGroup {
  return isReportingGroup(value) && reportingId(value.id) && reportingRevision(value.version)
    && (value.parentId === null || reportingId(value.parentId)) && Number.isSafeInteger(value.sortOrder);
}
export function validReportingDefinition(value: unknown): value is ReportingDefinition {
  return isReportingDefinition(value) && reportingId(value.id) && reportingId(value.groupId) && reportingId(value.dataSourceId)
    && reportingRevision(value.version) && Number.isSafeInteger(value.latestPublishedVersionNumber) && value.latestPublishedVersionNumber >= 0
    && value.definitionKey.trim().length > 0 && value.queryPortKey.trim().length > 0 && uniqueKeys(value.parameterSchema)
    && sameReportingLayout(value.layoutConfigJson, value.layoutConfigJson);
}
export function validReportingVersion(value: unknown): value is ReportingDefinitionVersion {
  return isReportingDefinitionVersion(value) && reportingId(value.id) && reportingId(value.definitionId) && reportingId(value.dataSourceId)
    && reportingId(value.publishedByUserId) && reportingRevision(value.versionNumber) && value.queryPortKey.trim().length > 0
    && uniqueKeys(value.parameterSchema) && sameReportingLayout(value.layoutConfigJson, value.layoutConfigJson);
}
export function validReportingPort(value: unknown): value is ReportingQueryPortDefinition {
  return isReportingQueryPortDefinition(value) && value.queryPortKey.trim().length > 0 && uniqueKeys(value.parameters)
    && value.supportedProviderKeys.length > 0 && value.supportedProviderKeys.every(key => key.trim().length > 0)
    && new Set(value.supportedProviderKeys).size === value.supportedProviderKeys.length;
}
/** 核对保存后的规范化配置；参数值和布局不进入错误回执。 */
export function matchesReportingDraft(value: ReportingDefinition, input: Omit<UpdateReportingDefinitionRequest, 'version'>): boolean {
  return sameReportingId(value.groupId, input.groupId) && sameReportingId(value.dataSourceId, input.dataSourceId)
    && value.name === input.name.trim() && value.description === (input.description?.trim() || null) && value.queryPortKey === input.queryPortKey.trim()
    && value.isEnabled === input.isEnabled && sameReportingParameters(value.parameterSchema, input.parameterSchema)
    && sameReportingLayout(value.layoutConfigJson, input.layoutConfigJson);
}
