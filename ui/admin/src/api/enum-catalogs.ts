import {
  isSettingsEnumCatalogDetail,
  isSettingsEnumCatalogSummary,
  type SettingsEnumCatalogDetail,
  type SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listSettingsEnumCatalogs(): Promise<SettingsEnumCatalogSummary[]> {
  const value = await request<unknown>('/api/v1/settings/enum-catalogs');
  if (!Array.isArray(value) || !value.every(isSettingsEnumCatalogSummary)) {
    throw new Error('client.invalid_settings_enum_catalog_page');
  }
  return value;
}

export async function getSettingsEnumCatalog(
  catalogKey: string
): Promise<SettingsEnumCatalogDetail> {
  const value = await request<unknown>(
    `/api/v1/settings/enum-catalogs/${encodeURIComponent(catalogKey)}`
  );
  if (!isSettingsEnumCatalogDetail(value)) {
    throw new Error('client.invalid_settings_enum_catalog');
  }
  return value;
}
