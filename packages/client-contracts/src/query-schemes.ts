import type { GridColumnPreference } from './grid-preferences.js';

export interface SavedQueryFilter { fieldKey: string; operatorKey: 'contains'; value: string }
export interface SavedQueryConfiguration { filters: SavedQueryFilter[]; sortBy: string; descending: boolean; columns: GridColumnPreference[] }
export interface SavedQueryDefinition { gridKey: string; schemaVersion: number; readPermission: string; supportsHost: boolean; supportsTenant: boolean;
  filters: { fieldKey: string; maxLength: number }[]; sortKeys: string[]; columnKeys: string[] }
export interface SavedQuerySummary { id: string; name: string; ownerUserId: string; schemaVersion: number; isShared: boolean; isCompatible: boolean;
  createdAtUtc: string; updatedAtUtc: string | null; version: number }
export interface SavedQueryDetail { summary: SavedQuerySummary; configuration: SavedQueryConfiguration | null }
export interface SavedQueryDefault { scheme: SavedQueryDetail | null; isUnavailable: boolean; version: number }
export interface SavePersonalQueryRequest { name: string; schemaVersion: number; configuration: SavedQueryConfiguration; version: number }

const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const key = (value: unknown): value is string => typeof value === 'string' && /^[a-zA-Z0-9_.-]{1,128}$/.test(value);
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const integer = (value: unknown, minimum = 0): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum;
const keys = (value: unknown): value is string[] => Array.isArray(value) && value.length > 0 && value.length <= 32 && value.every(key) && new Set(value).size === value.length;

/** 校验存储协议自身的预算和机器码；业务白名单由注册的当前 Schema 再次限制。 */
export function isSavedQueryConfiguration(value: unknown): value is SavedQueryConfiguration {
  if (!record(value) || !key(value.sortBy) || typeof value.descending !== 'boolean' || !Array.isArray(value.filters) || value.filters.length > 8
    || !Array.isArray(value.columns) || value.columns.length < 1 || value.columns.length > 32) return false;
  const filters = new Set<string>(), columns = new Set<string>(), orders = new Set<number>();
  return value.filters.every(filter => record(filter) && key(filter.fieldKey) && !filters.has(filter.fieldKey) && !!filters.add(filter.fieldKey)
    && filter.operatorKey === 'contains' && typeof filter.value === 'string' && filter.value.trim().length > 0 && filter.value.length <= 512 && !/[\u0000-\u001f\u007f]/.test(filter.value))
    && value.columns.every(column => record(column) && key(column.columnKey) && !columns.has(column.columnKey) && !!columns.add(column.columnKey)
      && integer(column.order) && column.order <= 31 && !orders.has(column.order) && !!orders.add(column.order)
      && (column.width === null || integer(column.width, 60) && column.width <= 1200) && typeof column.visible === 'boolean'
      && (column.fixed === null || column.fixed === 'left' || column.fixed === 'right')) && value.columns.some(column => column.visible === true);
}
export function isSavedQueryDefinition(value: unknown): value is SavedQueryDefinition {
  return record(value) && key(value.gridKey) && integer(value.schemaVersion, 1) && key(value.readPermission) && typeof value.supportsHost === 'boolean'
    && typeof value.supportsTenant === 'boolean' && (value.supportsHost || value.supportsTenant) && keys(value.sortKeys) && keys(value.columnKeys)
    && Array.isArray(value.filters) && value.filters.length <= 8 && value.filters.every(filter => record(filter) && key(filter.fieldKey) && integer(filter.maxLength, 1) && filter.maxLength <= 512)
    && new Set(value.filters.map(filter => filter.fieldKey)).size === value.filters.length;
}
export function isSavedQuerySummary(value: unknown): value is SavedQuerySummary {
  return record(value) && guid(value.id) && guid(value.ownerUserId) && typeof value.name === 'string' && value.name.length > 0 && value.name.length <= 64
    && integer(value.schemaVersion, 1) && integer(value.version, 1) && typeof value.isShared === 'boolean' && typeof value.isCompatible === 'boolean'
    && typeof value.createdAtUtc === 'string' && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}
export function isSavedQueryDetail(value: unknown): value is SavedQueryDetail {
  return record(value) && isSavedQuerySummary(value.summary) && (value.summary.isCompatible ? isSavedQueryConfiguration(value.configuration) : value.configuration === null);
}
export function isSavedQueryDefault(value: unknown): value is SavedQueryDefault {
  return record(value) && integer(value.version) && typeof value.isUnavailable === 'boolean'
    && (value.scheme === null || isSavedQueryDetail(value.scheme) && value.scheme.summary.isCompatible)
    && (!value.isUnavailable || value.scheme === null);
}

/** 应用前按业务当前白名单重验，不把共享配置当成执行权限或任意 URL 参数。 */
export function validateSavedQueryConfiguration(definition: SavedQueryDefinition, configuration: SavedQueryConfiguration): SavedQueryConfiguration {
  if (!isSavedQueryConfiguration(configuration) || !definition.sortKeys.includes(configuration.sortBy)
    || configuration.filters.some(filter => !definition.filters.some(field => field.fieldKey === filter.fieldKey && filter.value.length <= field.maxLength))
    || configuration.columns.some(column => !definition.columnKeys.includes(column.columnKey))) throw new TypeError('client.invalid_query_scheme');
  return { ...configuration, filters: configuration.filters.map(filter => ({ ...filter, value: filter.value.trim() })),
    columns: configuration.columns.map(column => ({ ...column })).sort((a, b) => a.order - b.order) };
}
