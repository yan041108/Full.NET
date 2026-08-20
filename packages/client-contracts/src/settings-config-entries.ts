export const SETTINGS_CONFIG_VALUE_KINDS = [
  'string',
  'boolean',
  'integer',
  'decimal',
  'json',
  'secret'
] as const;

export type SettingsConfigValueKind =
  (typeof SETTINGS_CONFIG_VALUE_KINDS)[number];

export interface SettingsConfigEntry {
  id: string;
  configKey: string;
  displayName: string;
  description: string | null;
  groupName: string | null;
  valueKind: SettingsConfigValueKind;
  value: string;
  hasValue: boolean;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface SettingsConfigEntryPage {
  items: SettingsConfigEntry[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateSettingsConfigEntryRequest {
  configKey: string;
  displayName: string;
  description?: string | null;
  groupName?: string | null;
  valueKind: SettingsConfigValueKind;
  value: string;
  displayOrder: number;
}

export interface UpdateSettingsConfigEntryRequest {
  displayName: string;
  description?: string | null;
  groupName?: string | null;
  value: string;
  displayOrder: number;
  version: number;
}

// 硬删除配置项请求，携带乐观锁版本用于并发控制，对应 Admin.NET DeleteConfig。
export interface DeleteSettingsConfigEntryRequest {
  version: number;
}

// 批量硬删除配置项请求；仅删除已禁用项，任一项未禁用则整体拒绝。
export interface BatchDeleteSettingsConfigEntriesRequest {
  ids: string[];
}

// 单个配置项值更新项，按 ConfigKey 定位并校验值类型后更新。
export interface ConfigValueUpdate {
  configKey: string;
  value: string;
}

// 批量更新配置项值请求，对应 Admin.NET 批量修改参数值。
export interface BatchUpdateConfigValuesRequest {
  updates: ConfigValueUpdate[];
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 与服务端 HostConfigEntryManagementService 的配置键规则一致：3-128 位小写。
const configKeyPattern = /^[a-z][a-z0-9._-]{1,126}[a-z0-9]$/;

export function isSettingsConfigValueKind(
  value: unknown
): value is SettingsConfigValueKind {
  return typeof value === 'string'
    && (SETTINGS_CONFIG_VALUE_KINDS as readonly string[]).includes(value);
}

export function isSettingsConfigEntry(
  value: unknown
): value is SettingsConfigEntry {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.configKey === 'string'
    && configKeyPattern.test(value.configKey)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && (value.groupName === null || typeof value.groupName === 'string')
    && isSettingsConfigValueKind(value.valueKind)
    && typeof value.value === 'string'
    && typeof value.hasValue === 'boolean'
    && Number.isInteger(value.displayOrder)
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isSettingsConfigEntryPage(
  value: unknown
): value is SettingsConfigEntryPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isSettingsConfigEntry)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isCreateSettingsConfigEntryRequest(
  value: unknown
): value is CreateSettingsConfigEntryRequest {
  return isRecord(value)
    && typeof value.configKey === 'string'
    && configKeyPattern.test(value.configKey)
    && isNonEmptyString(value.displayName)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && (value.groupName === undefined
      || value.groupName === null
      || typeof value.groupName === 'string')
    && isSettingsConfigValueKind(value.valueKind)
    && typeof value.value === 'string'
    && Number.isInteger(value.displayOrder);
}

export function isUpdateSettingsConfigEntryRequest(
  value: unknown
): value is UpdateSettingsConfigEntryRequest {
  return isRecord(value)
    && isNonEmptyString(value.displayName)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && (value.groupName === undefined
      || value.groupName === null
      || typeof value.groupName === 'string')
    && typeof value.value === 'string'
    && Number.isInteger(value.displayOrder)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

export function isDeleteSettingsConfigEntryRequest(
  value: unknown
): value is DeleteSettingsConfigEntryRequest {
  return isRecord(value)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

export function isConfigValueUpdate(
  value: unknown
): value is ConfigValueUpdate {
  return isRecord(value)
    && typeof value.configKey === 'string'
    && configKeyPattern.test(value.configKey)
    && typeof value.value === 'string';
}

export function isBatchDeleteSettingsConfigEntriesRequest(
  value: unknown
): value is BatchDeleteSettingsConfigEntriesRequest {
  return isRecord(value)
    && Array.isArray(value.ids)
    && value.ids.every((id): id is string => typeof id === 'string');
}

export function isBatchUpdateConfigValuesRequest(
  value: unknown
): value is BatchUpdateConfigValuesRequest {
  return isRecord(value)
    && Array.isArray(value.updates)
    && value.updates.every(isConfigValueUpdate);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
