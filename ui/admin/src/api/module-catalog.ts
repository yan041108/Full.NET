import {
  identityGetHostModule,
  identityListHostModules,
  isIdentityModuleCatalogEntry,
  type IdentityModuleCatalogEntry
} from '@fullnet/client-contracts';
import { http } from './http';

/** 查询 Identity 模块目录，并对生成守卫遗漏的关键字段约束补手写校验。 */
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

/** 查询单个 Identity 模块详情。 */
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

/** 导出模块目录条目模型，供模块列表、详情抽屉与健康能力展示复用同一契约。 */
export type { IdentityModuleCatalogEntry };
