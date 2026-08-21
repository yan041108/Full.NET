import {
  identityGetHostModule,
  identityListHostModules,
  isIdentityModuleCatalogEntry,
  type IdentityModuleCatalogEntry
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listIdentityModules(
  signal?: AbortSignal
): Promise<IdentityModuleCatalogEntry[]> {
  const value = await identityListHostModules(http, {}, signal);
  // 生成守卫不校验 moduleKey 模式与非空 displayName/version；页面仍要求手写契约。
  if (!Array.isArray(value) || !value.every(isIdentityModuleCatalogEntry)) {
    throw new Error('Invalid identity module catalog response.');
  }

  return value;
}

export async function getIdentityModule(
  moduleKey: string,
  signal?: AbortSignal
): Promise<IdentityModuleCatalogEntry> {
  const value = await identityGetHostModule(http, { moduleKey }, signal);
  if (!isIdentityModuleCatalogEntry(value)) {
    throw new Error('Invalid identity module catalog detail response.');
  }

  return value;
}
