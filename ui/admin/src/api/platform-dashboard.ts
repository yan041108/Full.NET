import {
  isHostDashboardSummary,
  platformGetHostDashboardSummary,
  type HostDashboardSummary
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getHostDashboardSummary(
  signal?: AbortSignal
): Promise<HostDashboardSummary> {
  const value = await platformGetHostDashboardSummary(http, {}, signal);
  if (!isHostDashboardSummary(value)) {
    throw new Error('client.invalid_host_dashboard_summary');
  }

  return value;
}

export type { HostDashboardSummary };
