import {
  isSettingsDictItem,
  isSettingsDictItemPage,
  isSettingsDictType,
  isSettingsDictTypePage,
  settingsCreateTenantDictItem,
  settingsCreateTenantDictType,
  settingsDeleteTenantDictItem,
  settingsDeleteTenantDictType,
  settingsDisableTenantDictItem,
  settingsDisableTenantDictType,
  settingsGetTenantDictItem,
  settingsListAllTenantDictTypes,
  settingsListTenantDictItems,
  settingsListTenantDictItemsByTypeCode,
  settingsListTenantDictTypes,
  settingsUpdateTenantDictItem,
  settingsUpdateTenantDictType,
  type SettingsDictItem,
  type SettingsDictItemPage,
  type SettingsDictType,
  type SettingsDictTypePage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listSettingsTenantDictTypes(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<SettingsDictTypePage> {
  const value = await settingsListTenantDictTypes(
    http,
    { page, pageSize },
    signal
  );
  if (!isSettingsDictTypePage(value)) {
    throw new Error('client.invalid_settings_dict_type_page');
  }

  return value;
}

export async function createSettingsTenantDictType(
  code: string,
  name: string,
  description: string | null,
  displayOrder: number,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsCreateTenantDictType(
    http,
    {
      body: {
        code,
        name,
        description: description?.trim() ? description.trim() : null,
        displayOrder
      }
    },
    signal
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }

  return value;
}

export async function updateSettingsTenantDictType(
  id: string,
  name: string,
  description: string | null,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsUpdateTenantDictType(
    http,
    {
      dictTypeId: id,
      body: { name, description, displayOrder, version }
    },
    signal
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }

  return value;
}

export async function disableSettingsTenantDictType(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsDisableTenantDictType(
    http,
    { dictTypeId: id },
    signal
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }

  return value;
}

export async function listSettingsTenantDictItems(
  dictTypeId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<SettingsDictItemPage> {
  const value = await settingsListTenantDictItems(
    http,
    { dictTypeId, page, pageSize },
    signal
  );
  if (!isSettingsDictItemPage(value)) {
    throw new Error('client.invalid_settings_dict_item_page');
  }

  return value;
}

export async function createSettingsTenantDictItem(
  dictTypeId: string,
  label: string,
  value: string,
  color: string | null,
  displayOrder: number,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsCreateTenantDictItem(
    http,
    {
      dictTypeId,
      body: {
        label,
        value,
        color: color?.trim() ? color.trim() : null,
        displayOrder
      }
    },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}

export async function updateSettingsTenantDictItem(
  id: string,
  label: string,
  color: string | null,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsUpdateTenantDictItem(
    http,
    {
      dictItemId: id,
      body: { label, color, displayOrder, version }
    },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}

export async function disableSettingsTenantDictItem(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsDisableTenantDictItem(
    http,
    { dictItemId: id },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}

export async function deleteSettingsTenantDictType(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await settingsDeleteTenantDictType(
    http,
    { dictTypeId: id, body: { version } },
    signal
  );
}

export async function deleteSettingsTenantDictItem(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await settingsDeleteTenantDictItem(
    http,
    { dictItemId: id, body: { version } },
    signal
  );
}

export async function listAllSettingsTenantDictTypes(
  signal?: AbortSignal
): Promise<SettingsDictType[]> {
  const value = await settingsListAllTenantDictTypes(http, {}, signal);
  if (!Array.isArray(value) || !value.every(isSettingsDictType)) {
    throw new Error('client.invalid_settings_dict_type_list');
  }

  return value;
}

export async function listSettingsTenantDictItemsByCode(
  code: string,
  signal?: AbortSignal
): Promise<SettingsDictItem[]> {
  const value = await settingsListTenantDictItemsByTypeCode(
    http,
    { code },
    signal
  );
  if (!Array.isArray(value) || !value.every(isSettingsDictItem)) {
    throw new Error('client.invalid_settings_dict_item_list');
  }

  return value;
}

export async function getSettingsTenantDictItem(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsGetTenantDictItem(
    http,
    { dictItemId: id },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}
