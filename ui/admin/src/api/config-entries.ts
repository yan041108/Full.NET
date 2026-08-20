import {
  settingsBatchDeleteHostConfigEntries,
  settingsBatchUpdateHostConfigEntryValues,
  settingsCreateHostConfigEntry,
  settingsDeleteHostConfigEntry,
  settingsDisableHostConfigEntry,
  settingsListAllHostConfigEntries,
  settingsListHostConfigEntries,
  settingsListHostConfigEntryGroups,
  settingsUpdateHostConfigEntry,
  type ConfigValueUpdate,
  type SettingsConfigEntry,
  type SettingsConfigEntryPage,
  type SettingsConfigValueKind
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listSettingsConfigEntries(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<SettingsConfigEntryPage> {
  return settingsListHostConfigEntries(http, { page, pageSize }, signal);
}

// 全量配置项列表（不分页），对应 Admin.NET queryConfigList 全量场景。
export async function listAllSettingsConfigEntries(
  signal?: AbortSignal
): Promise<SettingsConfigEntry[]> {
  return settingsListAllHostConfigEntries(http, {}, signal);
}

// 查询已使用的配置分组去重列表，对应 Admin.NET 配置分组下拉。
export async function listSettingsConfigGroups(signal?: AbortSignal): Promise<string[]> {
  return settingsListHostConfigEntryGroups(http, {}, signal);
}

export async function createSettingsConfigEntry(
  configKey: string,
  displayName: string,
  description: string | null,
  valueKind: SettingsConfigValueKind,
  value: string,
  displayOrder: number,
  groupName?: string | null,
  signal?: AbortSignal
): Promise<SettingsConfigEntry> {
  return settingsCreateHostConfigEntry(http, {
    body: {
      configKey,
      displayName,
      description: description?.trim() ? description.trim() : null,
      groupName: groupName?.trim() ? groupName.trim() : null,
      valueKind,
      value,
      displayOrder
    }
  }, signal);
}

export async function updateSettingsConfigEntry(
  id: string,
  displayName: string,
  description: string | null,
  value: string,
  displayOrder: number,
  version: number,
  groupName?: string | null,
  signal?: AbortSignal
): Promise<SettingsConfigEntry> {
  return settingsUpdateHostConfigEntry(http, {
    configEntryId: id,
    body: {
      displayName,
      description,
      groupName: groupName?.trim() ? groupName.trim() : null,
      value,
      displayOrder,
      version
    }
  }, signal);
}

export async function disableSettingsConfigEntry(
  id: string,
  signal?: AbortSignal
): Promise<SettingsConfigEntry> {
  return settingsDisableHostConfigEntry(http, { configEntryId: id }, signal);
}

// 硬删除已禁用的配置项，携带乐观锁版本用于并发控制。
export async function deleteSettingsConfigEntry(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await settingsDeleteHostConfigEntry(
    http,
    { configEntryId: id, body: { version } },
    signal
  );
}

// 批量硬删除已禁用的配置项；仅删除 IsActive=0 的项，任一未禁用则整体拒绝。
export async function batchDeleteSettingsConfigEntries(
  ids: string[],
  signal?: AbortSignal
): Promise<void> {
  await settingsBatchDeleteHostConfigEntries(http, { body: { ids } }, signal);
}

// 批量更新配置项值，按 ConfigKey 定位并校验值类型后更新。
export async function batchUpdateConfigValues(
  updates: ConfigValueUpdate[],
  signal?: AbortSignal
): Promise<void> {
  await settingsBatchUpdateHostConfigEntryValues(
    http,
    { body: { updates } },
    signal
  );
}
