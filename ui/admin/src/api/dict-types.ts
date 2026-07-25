import {
  isSettingsDictItem,
  isSettingsDictItemPage,
  isSettingsDictType,
  isSettingsDictTypePage,
  type SettingsDictItem,
  type SettingsDictItemPage,
  type SettingsDictType,
  type SettingsDictTypePage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listSettingsDictTypes(
  page = 1,
  pageSize = 20
): Promise<SettingsDictTypePage> {
  const value = await request<unknown>(
    `/api/v1/settings/dict-types?page=${page}&pageSize=${pageSize}`
  );
  if (!isSettingsDictTypePage(value)) {
    throw new Error('client.invalid_settings_dict_type_page');
  }
  return value;
}

export async function createSettingsDictType(
  code: string,
  name: string,
  description: string | null,
  displayOrder: number
): Promise<SettingsDictType> {
  const value = await request<unknown>('/api/v1/settings/dict-types', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      code,
      name,
      description: description?.trim() ? description.trim() : null,
      displayOrder
    })
  });
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }
  return value;
}

export async function updateSettingsDictType(
  id: string,
  name: string,
  description: string | null,
  displayOrder: number,
  version: number
): Promise<SettingsDictType> {
  const value = await request<unknown>(
    `/api/v1/settings/dict-types/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ name, description, displayOrder, version })
    }
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }
  return value;
}

export async function disableSettingsDictType(
  id: string
): Promise<SettingsDictType> {
  const value = await request<unknown>(
    `/api/v1/settings/dict-types/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }
  return value;
}

export async function listSettingsDictItems(
  dictTypeId: string,
  page = 1,
  pageSize = 20
): Promise<SettingsDictItemPage> {
  const value = await request<unknown>(
    `/api/v1/settings/dict-types/${encodeURIComponent(dictTypeId)}/items?page=${page}&pageSize=${pageSize}`
  );
  if (!isSettingsDictItemPage(value)) {
    throw new Error('client.invalid_settings_dict_item_page');
  }
  return value;
}

export async function createSettingsDictItem(
  dictTypeId: string,
  label: string,
  value: string,
  color: string | null,
  displayOrder: number
): Promise<SettingsDictItem> {
  const response = await request<unknown>(
    `/api/v1/settings/dict-types/${encodeURIComponent(dictTypeId)}/items`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        label,
        value,
        color: color?.trim() ? color.trim() : null,
        displayOrder
      })
    }
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }
  return response;
}

export async function updateSettingsDictItem(
  id: string,
  label: string,
  color: string | null,
  displayOrder: number,
  version: number
): Promise<SettingsDictItem> {
  const response = await request<unknown>(
    `/api/v1/settings/dict-items/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ label, color, displayOrder, version })
    }
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }
  return response;
}

export async function disableSettingsDictItem(
  id: string
): Promise<SettingsDictItem> {
  const response = await request<unknown>(
    `/api/v1/settings/dict-items/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }
  return response;
}
