import type { PagedResult } from './paged-result.js';
import { isReportingParameterSchemaEntry, type ReportingParameterSchemaEntry } from './reporting-definitions.js';

/** 租户发布目录只公开执行选择所需的版本和参数，不携带数据源或草稿配置。 */
export interface ReportingPublishedDefinition {
  id: string;
  definitionKey: string;
  name: string;
  versionNumber: number;
  parameterSchema: ReportingParameterSchemaEntry[];
}
export interface ReportingDefinitionTenantGrant {
  id: string;
  definitionId: string;
  tenantId: string;
  isEnabled: boolean;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}
function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}
function guid(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
export function isReportingPublishedDefinition(value: unknown): value is ReportingPublishedDefinition {
  return record(value) && guid(value.id) && typeof value.definitionKey === 'string' && typeof value.name === 'string'
    && Number.isInteger(value.versionNumber) && Number(value.versionNumber) > 0
    && Array.isArray(value.parameterSchema) && value.parameterSchema.every(isReportingParameterSchemaEntry);
}
export function isReportingDefinitionTenantGrant(value: unknown): value is ReportingDefinitionTenantGrant {
  return record(value) && guid(value.id) && guid(value.definitionId) && guid(value.tenantId)
    && typeof value.isEnabled === 'boolean' && Number.isInteger(value.version) && Number(value.version) > 0
    && typeof value.createdAtUtc === 'string' && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}
export function isReportingPublicationPage<T>(value: unknown, guard: (item: unknown) => item is T): value is PagedResult<T> {
  return record(value) && Array.isArray(value.items) && value.items.every(guard)
    && Number.isInteger(value.page) && Number(value.page) >= 1
    && Number.isInteger(value.pageSize) && Number(value.pageSize) >= 1
    && Number.isInteger(value.total) && Number(value.total) >= 0;
}
