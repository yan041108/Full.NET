import { request } from './http';
import {
  isHostDashboardSummary,
  type HostDashboardSummary
} from '@fullnet/client-contracts';

export async function getHostDashboardSummary(): Promise<HostDashboardSummary> {
  const value = await request<unknown>('/api/v1/platform/host-dashboard-summary');
  if (!isHostDashboardSummary(value)) {
    throw new Error('Invalid host dashboard summary payload.');
  }
  return value;
}
