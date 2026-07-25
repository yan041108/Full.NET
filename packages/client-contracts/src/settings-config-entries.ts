export const SETTINGS_CONFIG_VALUE_KINDS = [
  'string',
  'boolean',
  'integer',
  'decimal',
  'json'
] as const;

export type SettingsConfigValueKind =
  (typeof SETTINGS_CONFIG_VALUE_KINDS)[number];

export interface SettingsConfigEntry {
  id: string;
  configKey: string;
  displayName: string;
  description: string | null;
  valueKind: SettingsConfigValueKind;
  value: string;
  displayOrder: number;
  isActive: boolean;
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
  valueKind: SettingsConfigValueKind;
  value: string;
  displayOrder: number;
}

export interface UpdateSettingsConfigEntryRequest {
  displayName: string;
  description?: string | null;
  value: string;
  displayOrder: number;
  version: number;
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
    && isSettingsConfigValueKind(value.valueKind)
    && typeof value.value === 'string'
    && Number.isInteger(value.displayOrder)
    && typeof value.isActive === 'boolean'
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
    && typeof value.value === 'string'
    && Number.isInteger(value.displayOrder)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
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
