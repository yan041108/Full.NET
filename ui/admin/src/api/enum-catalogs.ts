import {
  isSettingsEnumCatalogDetail,
  isSettingsEnumCatalogSummary,
  settingsGetHostEnumCatalog,
  settingsListHostEnumCatalogs,
  type SettingsEnumCatalogDetail,
  type SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listSettingsEnumCatalogs(
  signal?: AbortSignal
): Promise<SettingsEnumCatalogSummary[]> {
  const value = await settingsListHostEnumCatalogs(http, {}, signal);
  if (!Array.isArray(value) || !value.every(isSettingsEnumCatalogSummary)) {
    throw new Error('client.invalid_settings_enum_catalog_page');
  }

  return value;
}

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
