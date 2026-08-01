import type { IdentityModuleCatalogEntry } from '@fullnet/client-contracts';
import { isIdentityModuleCatalogEntry } from '@fullnet/client-contracts';
import { request } from './http';

export async function listIdentityModules(): Promise<IdentityModuleCatalogEntry[]> {
  const value = await request<unknown>('/api/v1/identity/modules');
  if (!Array.isArray(value) || !value.every(isIdentityModuleCatalogEntry)) {
    throw new Error('Invalid identity module catalog response.');
  }

  return value;
}

export async function getIdentityModule(
  moduleKey: string
): Promise<IdentityModuleCatalogEntry> {
  const value = await request<unknown>(
    `/api/v1/identity/modules/${encodeURIComponent(moduleKey)}`
  );
  if (!isIdentityModuleCatalogEntry(value)) {
    throw new Error('Invalid identity module catalog detail response.');
  }

  return value;
}
