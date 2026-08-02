import { request } from './http';
import {
  isHostJobSchedule,
  isHostJobSchedulePage,
  type HostJobSchedule,
  type HostJobSchedulePage
} from '@fullnet/client-contracts';

export async function listHostJobSchedules(
  jobDefinitionId?: string,
  page = 1,
  pageSize = 20
): Promise<HostJobSchedulePage> {
  const query = jobDefinitionId
    ? `&jobDefinitionId=${encodeURIComponent(jobDefinitionId)}`
    : '';
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules?page=${page}&pageSize=${pageSize}${query}`
  );
  if (!isHostJobSchedulePage(value)) {
    throw new Error('Invalid host job schedule page response');
  }
  return value;
}

export async function createHostJobSchedule(
  jobDefinitionId: string,
  triggerKind: string,
  timeZoneId: string,
  misfirePolicy: string,
  cronExpression?: string | null,
  oneTimeAtUtc?: string | null
): Promise<HostJobSchedule> {
  const value = await request<unknown>('/api/v1/jobs/host-schedules', {
    method: 'POST',
    body: JSON.stringify({
      jobDefinitionId,
      triggerKind,
      cronExpression: cronExpression ?? null,
      timeZoneId,
      oneTimeAtUtc: oneTimeAtUtc ?? null,
      misfirePolicy
    })
  });
  if (!isHostJobSchedule(value)) {
    throw new Error('Invalid host job schedule payload.');
  }
  return value;
}

export async function updateHostJobSchedule(
  id: string,
  triggerKind: string,
  timeZoneId: string,
  misfirePolicy: string,
  version: number,
  cronExpression?: string | null,
  oneTimeAtUtc?: string | null
): Promise<HostJobSchedule> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      body: JSON.stringify({
        triggerKind,
        cronExpression: cronExpression ?? null,
        timeZoneId,
        oneTimeAtUtc: oneTimeAtUtc ?? null,
        misfirePolicy,
        version
      })
    }
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('Invalid host job schedule payload.');
  }
  return value;
}

export async function pauseHostJobSchedule(
  id: string,
  version: number
): Promise<HostJobSchedule> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules/${encodeURIComponent(id)}/pause`,
    {
      method: 'POST',
      body: JSON.stringify({ version })
    }
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('Invalid host job schedule payload.');
  }
  return value;
}

export async function resumeHostJobSchedule(
  id: string,
  version: number
): Promise<HostJobSchedule> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules/${encodeURIComponent(id)}/resume`,
    {
      method: 'POST',
      body: JSON.stringify({ version })
    }
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('Invalid host job schedule payload.');
  }
  return value;
}
