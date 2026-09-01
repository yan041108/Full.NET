import {
  isSettingsEnumCatalogDetail,
  isSettingsEnumCatalogSummary,
  settingsGetHostEnumCatalog,
  settingsListHostEnumCatalogs,
  type SettingsEnumCatalogDetail,
  type SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询枚举目录摘要列表，并对每个条目做失败关闭校验。 */
export async function listSettingsEnumCatalogs(
  signal?: AbortSignal
): Promise<SettingsEnumCatalogSummary[]> {
  const value = await settingsListHostEnumCatalogs(http, {}, signal);
  if (!Array.isArray(value) || !value.every(isSettingsEnumCatalogSummary)) {
    throw new Error('client.invalid_settings_enum_catalog_page');
  }

  return value;
}

/** 查询单个枚举目录详情。 */
export async function getSettingsEnumCatalog(
  catalogKey: string,
  signal?: AbortSignal
): Promise<SettingsEnumCatalogDetail> {
  const value = await settingsGetHostEnumCatalog(
    http,
    { catalogKey },
    signal
  );
  if (!isSettingsEnumCatalogDetail(value)) {
    throw new Error('client.invalid_settings_enum_catalog');
  }

  return value;
}

/** 导出枚举目录摘要与详情模型，供目录列表、详情抽屉与值类型提示共享同一契约。 */
export type {
  SettingsEnumCatalogDetail,
  SettingsEnumCatalogSummary
};
