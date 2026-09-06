import {
  observabilityGetServerRuntime,
  observabilityListServerInstances,
  type ServerInstanceCatalogEntry,
  type ServerRuntimeSnapshot
} from '@fullnet/client-contracts';
import { http } from './http';

/** 列出服务器实例目录，供监控页选择实例并区分本地可查询项。 */
export function listObservabilityServerInstances(
  signal?: AbortSignal
): Promise<ServerInstanceCatalogEntry[]> {
  return observabilityListServerInstances(http, {}, signal);
}

/** 读取指定实例的运行时快照；仅当前进程实例返回实时指标。 */
export function getObservabilityServerRuntime(
  instanceKey: string,
  signal?: AbortSignal
): Promise<ServerRuntimeSnapshot> {
  return observabilityGetServerRuntime(http, { instanceKey }, signal);
}

/** 导出服务器监控契约模型，供实例目录与运行时卡片共享。 */
export type { ServerInstanceCatalogEntry, ServerRuntimeSnapshot };
