import {
  isHostDashboardSummary,
  platformGetHostDashboardSummary,
  type HostDashboardSummary
} from '@fullnet/client-contracts';
import { http } from './http';

/** 读取平台工作台摘要，并对响应结构做失败关闭校验。 */
export async function getHostDashboardSummary(
  signal?: AbortSignal
): Promise<HostDashboardSummary> {
  const value = await platformGetHostDashboardSummary(http, {}, signal);
  if (!isHostDashboardSummary(value)) {
    throw new Error('client.invalid_host_dashboard_summary');
  }

  return value;
}

/** 导出工作台摘要模型，供首页卡片与最近活动面板复用同一快照结构。 */
export type { HostDashboardSummary };
