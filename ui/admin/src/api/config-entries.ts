import {
  isSettingsConfigEntry,
  isSettingsConfigEntryPage,
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

export async function createSettingsConfigEntry(
  configKey: string,
  displayName: string,
  description: string | null,
  valueKind: SettingsConfigValueKind,
  value: string,
  displayOrder: number
): Promise<SettingsConfigEntry> {
  const response = await request<unknown>('/api/v1/settings/config-entries', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      configKey,
      displayName,
      description: description?.trim() ? description.trim() : null,
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
  version: number
): Promise<SettingsConfigEntry> {
  const response = await request<unknown>(
    `/api/v1/settings/config-entries/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        displayName,
        description,
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
