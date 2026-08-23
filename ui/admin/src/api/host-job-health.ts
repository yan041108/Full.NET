import {
  isHostJobHealth,
  jobsGetHostJobHealth,
  type HostJobHealth
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getHostJobHealth(
  signal?: AbortSignal
): Promise<HostJobHealth> {
  const value = await jobsGetHostJobHealth(http, {}, signal);
  if (!isHostJobHealth(value)) {
    throw new Error('client.invalid_host_job_health');
  }

  return value;
}

export type { HostJobHealth };
