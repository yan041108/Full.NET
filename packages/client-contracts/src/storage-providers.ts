export interface StorageProviderCatalogItem {
  providerKey: string;
  displayName: string;
  kind: 'local' | 's3' | 'oss' | string;
  isDefault: boolean;
  isConfigured: boolean;
  configurationSummary: string | null;
  supportsConnectivityTest: boolean;
}

export interface TestStorageProviderConnectivityResult {
  succeeded: boolean;
  message: string;
}

export function isStorageProviderCatalogItem(value: unknown): value is StorageProviderCatalogItem {
  return isRecord(value)
    && typeof value.providerKey === 'string'
    && typeof value.displayName === 'string'
    && typeof value.kind === 'string'
    && typeof value.isDefault === 'boolean'
    && typeof value.isConfigured === 'boolean'
    && (value.configurationSummary === null || typeof value.configurationSummary === 'string')
    && typeof value.supportsConnectivityTest === 'boolean';
}

export function isStorageProviderCatalogItemList(
  value: unknown
): value is StorageProviderCatalogItem[] {
  return Array.isArray(value) && value.every(isStorageProviderCatalogItem);
}

export function isTestStorageProviderConnectivityResult(
  value: unknown
): value is TestStorageProviderConnectivityResult {
  return isRecord(value)
    && typeof value.succeeded === 'boolean'
    && typeof value.message === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
