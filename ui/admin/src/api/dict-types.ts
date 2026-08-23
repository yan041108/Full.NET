import {
  isSettingsDictItem,
  isSettingsDictItemPage,
  isSettingsDictType,
  isSettingsDictTypePage,
  settingsCreateHostDictItem,
  settingsCreateHostDictType,
  settingsDeleteHostDictItem,
  settingsDeleteHostDictType,
  settingsDisableHostDictItem,
  settingsDisableHostDictType,
  settingsGetHostDictItem,
  settingsListAllHostDictTypes,
  settingsListHostDictItems,
  settingsListHostDictItemsByTypeCode,
  settingsListHostDictTypes,
  settingsUpdateHostDictItem,
  settingsUpdateHostDictType,
  type SettingsDictItem,
  type SettingsDictItemPage,
  type SettingsDictType,
  type SettingsDictTypePage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listSettingsDictTypes(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<SettingsDictTypePage> {
  const value = await settingsListHostDictTypes(
    http,
    { page, pageSize },
    signal
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
  displayOrder: number,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsCreateHostDictType(
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

export async function updateSettingsDictType(
  id: string,
  name: string,
  description: string | null,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsUpdateHostDictType(
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

export async function disableSettingsDictType(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictType> {
  const value = await settingsDisableHostDictType(
    http,
    { dictTypeId: id },
    signal
  );
  if (!isSettingsDictType(value)) {
    throw new Error('client.invalid_settings_dict_type');
  }

  return value;
}

export async function listSettingsDictItems(
  dictTypeId: string,
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<SettingsDictItemPage> {
  const value = await settingsListHostDictItems(
    http,
    { dictTypeId, page, pageSize },
    signal
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
  displayOrder: number,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsCreateHostDictItem(
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

export async function updateSettingsDictItem(
  id: string,
  label: string,
  color: string | null,
  displayOrder: number,
  version: number,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsUpdateHostDictItem(
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

export async function disableSettingsDictItem(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsDisableHostDictItem(
    http,
    { dictItemId: id },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}

// 硬删除已禁用的字典类型，携带乐观锁版本用于并发控制。
export async function deleteSettingsDictType(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await settingsDeleteHostDictType(
    http,
    { dictTypeId: id, body: { version } },
    signal
  );
}

// 硬删除已禁用的字典项，携带乐观锁版本用于并发控制。
export async function deleteSettingsDictItem(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await settingsDeleteHostDictItem(
    http,
    { dictItemId: id, body: { version } },
    signal
  );
}

// 全量字典类型列表（不分页），供下拉与全量消费场景使用。
export async function listAllSettingsDictTypes(
  signal?: AbortSignal
): Promise<SettingsDictType[]> {
  const value = await settingsListAllHostDictTypes(http, {}, signal);
  if (!Array.isArray(value) || !value.every(isSettingsDictType)) {
    throw new Error('client.invalid_settings_dict_type_list');
  }

  return value;
}

// 按字典类型编码查询启用字典项，对应 Admin.NET dataList by code。
export async function listSettingsDictItemsByCode(
  code: string,
  signal?: AbortSignal
): Promise<SettingsDictItem[]> {
  const value = await settingsListHostDictItemsByTypeCode(
    http,
    { code },
    signal
  );
  if (!Array.isArray(value) || !value.every(isSettingsDictItem)) {
    throw new Error('client.invalid_settings_dict_item_list');
  }

  return value;
}

// 查询单个字典项详情。
export async function getSettingsDictItem(
  id: string,
  signal?: AbortSignal
): Promise<SettingsDictItem> {
  const response = await settingsGetHostDictItem(
    http,
    { dictItemId: id },
    signal
  );
  if (!isSettingsDictItem(response)) {
    throw new Error('client.invalid_settings_dict_item');
  }

  return response;
}
