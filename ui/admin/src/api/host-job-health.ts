import { request } from './http';
import {
  isHostJobHealth,
  type HostJobHealth
} from '@fullnet/client-contracts';

export async function getHostJobHealth(): Promise<HostJobHealth> {
  const value = await request<unknown>('/api/v1/jobs/host-health');
  if (!isHostJobHealth(value)) {
    throw new Error('Invalid host job health response');
  }
  return value;
}
