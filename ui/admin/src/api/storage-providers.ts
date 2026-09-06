import {
  isStorageProviderCatalogItemList,
  isTestStorageProviderConnectivityResult,
  type StorageProviderCatalogItem,
  type TestStorageProviderConnectivityResult
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listStorageProviders(
  signal?: AbortSignal
): Promise<StorageProviderCatalogItem[]> {
  const value = await request<unknown>(
    '/api/v1/files/storage-providers',
    { method: 'GET' },
    signal
  );
  if (!isStorageProviderCatalogItemList(value)) {
    throw new Error('client.invalid_storage_provider_catalog');
  }
  return value;
}

export async function testStorageProviderConnectivity(
  providerKey: string,
  signal?: AbortSignal
): Promise<TestStorageProviderConnectivityResult> {
  const value = await request<unknown>(
    `/api/v1/files/storage-providers/${encodeURIComponent(providerKey)}/test`,
    { method: 'POST' },
    signal
  );
  if (!isTestStorageProviderConnectivityResult(value)) {
    throw new Error('client.invalid_storage_provider_test_result');
  }
  return value;
}
