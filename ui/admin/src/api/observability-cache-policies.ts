import {
  observabilityGetCachePolicy,
  observabilityInvalidateCachePolicy,
  observabilityListCachePolicies,
  type CacheInvalidationRequest,
  type CacheInvalidationResult,
  type CachePolicySummary
} from '@fullnet/client-contracts';
import { http } from './http';

/** 列出已登记缓存策略目录，供缓存管理页初始化。 */
export function listObservabilityCachePolicies(
  signal?: AbortSignal
): Promise<CachePolicySummary[]> {
  return observabilityListCachePolicies(http, {}, signal);
}

/** 读取单条缓存策略详情。 */
export function getObservabilityCachePolicy(
  entryName: string,
  signal?: AbortSignal
): Promise<CachePolicySummary> {
  return observabilityGetCachePolicy(http, { entryName }, signal);
}

/** 执行登记的精确失效操作。 */
export function invalidateObservabilityCachePolicy(
  entryName: string,
  request: CacheInvalidationRequest,
  signal?: AbortSignal
): Promise<CacheInvalidationResult> {
  return observabilityInvalidateCachePolicy(http, { entryName, body: request }, signal);
}

/** 导出缓存策略与失效结果模型，供缓存管理页共享契约。 */
export type { CacheInvalidationRequest, CacheInvalidationResult, CachePolicySummary };
