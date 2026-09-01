import {
  isHostJobHealth,
  jobsGetHostJobHealth,
  type HostJobHealth
} from '@fullnet/client-contracts';
import { http } from './http';

/** 读取 Host 作业健康摘要，并对响应结构做失败关闭校验。 */
export async function getHostJobHealth(
  signal?: AbortSignal
): Promise<HostJobHealth> {
  const value = await jobsGetHostJobHealth(http, {}, signal);
  if (!isHostJobHealth(value)) {
    throw new Error('client.invalid_host_job_health');
  }

  return value;
}

/** 导出作业健康聚合模型，供作业监控页与轮询器共享同一权威摘要结构。 */
export type { HostJobHealth };
