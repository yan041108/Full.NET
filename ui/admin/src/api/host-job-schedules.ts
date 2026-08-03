import { request } from './http';
import {
  isHostJobSchedule,
  isHostJobScheduleCronPreview,
  isHostJobScheduleDefinitionOptionList,
  isHostJobSchedulePage,
  type HostJobSchedule,
  type HostJobScheduleCronPreview,
  type HostJobScheduleDefinitionOption,
  type HostJobSchedulePage
} from '@fullnet/client-contracts';

export interface ListHostJobSchedulesParams {
  page?: number;
  pageSize?: number;
  jobDefinitionId?: string;
  search?: string;
  isEnabled?: boolean;
  triggerKind?: string;
}

function buildScheduleListQuery(params: ListHostJobSchedulesParams): string {
  const query = new URLSearchParams();
  query.set('page', String(params.page ?? 1));
  query.set('pageSize', String(params.pageSize ?? 20));
  if (params.jobDefinitionId) {
    query.set('jobDefinitionId', params.jobDefinitionId);
  }
  if (params.search?.trim()) {
    query.set('search', params.search.trim());
  }
  if (params.isEnabled !== undefined) {
    query.set('isEnabled', String(params.isEnabled));
  }
  if (params.triggerKind) {
    query.set('triggerKind', params.triggerKind);
  }
  return query.toString();
}

export async function listHostJobSchedules(
  params: ListHostJobSchedulesParams = {}
): Promise<HostJobSchedulePage> {
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules?${buildScheduleListQuery(params)}`
  );
  if (!isHostJobSchedulePage(value)) {
    throw new Error('Invalid host job schedule page response');
  }
  return value;
}

export async function listHostJobScheduleDefinitionOptions(): Promise<
  HostJobScheduleDefinitionOption[]
> {
  const value = await request<unknown>(
    '/api/v1/jobs/host-schedules/definition-options'
  );
  if (!isHostJobScheduleDefinitionOptionList(value)) {
    throw new Error('Invalid host job schedule definition options response');
  }
  return value;
}

export async function previewHostJobScheduleCron(
  cronExpression: string,
  timeZoneId: string
): Promise<HostJobScheduleCronPreview> {
  const query = new URLSearchParams({
    cronExpression,
    timeZoneId
  });
  const value = await request<unknown>(
    `/api/v1/jobs/host-schedules/cron-preview?${query.toString()}`
  );
  if (!isHostJobScheduleCronPreview(value)) {
    throw new Error('Invalid host job schedule cron preview response');
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
