import {
  isSettingsConfigEntry,
  isSettingsConfigEntryPage,
  type ConfigValueUpdate,
  type SettingsConfigEntry,
  type SettingsConfigEntryPage,
  type SettingsConfigValueKind
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listSettingsConfigEntries(
  page = 1,
  pageSize = 20
): Promise<SettingsConfigEntryPage> {
  const value = await request<unknown>(
    `/api/v1/settings/config-entries?page=${page}&pageSize=${pageSize}`
  );
  if (!isSettingsConfigEntryPage(value)) {
    throw new Error('client.invalid_settings_config_entry_page');
  }
  return value;
}

// 全量配置项列表（不分页），对应 Admin.NET queryConfigList 全量场景。
export async function listAllSettingsConfigEntries(): Promise<SettingsConfigEntry[]> {
  const value = await request<unknown>('/api/v1/settings/config-entries/list');
  if (!Array.isArray(value) || !value.every(isSettingsConfigEntry)) {
    throw new Error('client.invalid_settings_config_entry_list');
  }
  return value;
}

// 查询已使用的配置分组去重列表，对应 Admin.NET 配置分组下拉。
export async function listSettingsConfigGroups(): Promise<string[]> {
  const value = await request<unknown>('/api/v1/settings/config-entries/groups');
  if (!Array.isArray(value) || !value.every((item): item is string => typeof item === 'string')) {
    throw new Error('client.invalid_settings_config_entry_groups');
  }
  return value;
}

export async function createSettingsConfigEntry(
  configKey: string,
  displayName: string,
  description: string | null,
  valueKind: SettingsConfigValueKind,
  value: string,
  displayOrder: number,
  groupName?: string | null
): Promise<SettingsConfigEntry> {
  const response = await request<unknown>('/api/v1/settings/config-entries', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      configKey,
      displayName,
      description: description?.trim() ? description.trim() : null,
      groupName: groupName?.trim() ? groupName.trim() : null,
      valueKind,
      value,
      displayOrder
    })
  });
  if (!isSettingsConfigEntry(response)) {
    throw new Error('client.invalid_settings_config_entry');
  }
  return response;
}

export async function updateSettingsConfigEntry(
  id: string,
  displayName: string,
  description: string | null,
  value: string,
  displayOrder: number,
  version: number,
  groupName?: string | null
): Promise<SettingsConfigEntry> {
  const response = await request<unknown>(
    `/api/v1/settings/config-entries/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        displayName,
        description,
        groupName: groupName?.trim() ? groupName.trim() : null,
        value,
        displayOrder,
        version
      })
    }
  );
  if (!isSettingsConfigEntry(response)) {
    throw new Error('client.invalid_settings_config_entry');
  }
  return response;
}

export async function disableSettingsConfigEntry(
  id: string
): Promise<SettingsConfigEntry> {
  const response = await request<unknown>(
    `/api/v1/settings/config-entries/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isSettingsConfigEntry(response)) {
    throw new Error('client.invalid_settings_config_entry');
  }
  return response;
}

// 硬删除已禁用的配置项，携带乐观锁版本用于并发控制。
export async function deleteSettingsConfigEntry(
  id: string,
  version: number
): Promise<void> {
  await request<unknown>(
    `/api/v1/settings/config-entries/${encodeURIComponent(id)}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    }
  );
}

// 批量硬删除已禁用的配置项；仅删除 IsActive=0 的项，任一未禁用则整体拒绝。
export async function batchDeleteSettingsConfigEntries(
  ids: string[]
): Promise<void> {
  await request<unknown>('/api/v1/settings/config-entries/batch-delete', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ ids })
  });
}

// 批量更新配置项值，按 ConfigKey 定位并校验值类型后更新。
export async function batchUpdateConfigValues(
  updates: ConfigValueUpdate[]
): Promise<void> {
  await request<unknown>('/api/v1/settings/config-entries/batch-update-values', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ updates })
  });
}
