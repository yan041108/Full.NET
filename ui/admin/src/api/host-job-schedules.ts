import {
  isHostJobSchedule,
  isHostJobScheduleCronPreview,
  isHostJobScheduleDefinitionOptionList,
  isHostJobSchedulePage,
  jobsCreateHostJobSchedule,
  jobsDeleteHostJobSchedule,
  jobsListHostJobScheduleDefinitionOptions,
  jobsListHostJobSchedules,
  jobsPauseHostJobSchedule,
  jobsPreviewHostJobScheduleCron,
  jobsResumeHostJobSchedule,
  jobsUpdateHostJobSchedule,
  type HostJobSchedule,
  type HostJobScheduleCronPreview,
  type HostJobScheduleDefinitionOption,
  type HostJobSchedulePage
} from '@fullnet/client-contracts';
import { http } from './http';

export interface ListHostJobSchedulesParams {
  page?: number;
  pageSize?: number;
  jobDefinitionId?: string;
  search?: string;
  isEnabled?: boolean;
  triggerKind?: string;
}

export async function listHostJobSchedules(
  params: ListHostJobSchedulesParams = {},
  signal?: AbortSignal
): Promise<HostJobSchedulePage> {
  const search = params.search?.trim();
  const value = await jobsListHostJobSchedules(
    http,
    {
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 20,
      jobDefinitionId: params.jobDefinitionId,
      search: search ? search : undefined,
      isEnabled: params.isEnabled,
      triggerKind: params.triggerKind
    },
    signal
  );
  if (!isHostJobSchedulePage(value)) {
    throw new Error('client.invalid_host_job_schedule_page');
  }

  return value;
}

export async function listHostJobScheduleDefinitionOptions(
  signal?: AbortSignal
): Promise<HostJobScheduleDefinitionOption[]> {
  const value = await jobsListHostJobScheduleDefinitionOptions(http, {}, signal);
  if (!isHostJobScheduleDefinitionOptionList(value)) {
    throw new Error('client.invalid_host_job_schedule_definition_options');
  }

  return value;
}

export async function previewHostJobScheduleCron(
  cronExpression: string,
  timeZoneId: string,
  signal?: AbortSignal
): Promise<HostJobScheduleCronPreview> {
  const value = await jobsPreviewHostJobScheduleCron(
    http,
    { cronExpression, timeZoneId },
    signal
  );
  if (!isHostJobScheduleCronPreview(value)) {
    throw new Error('client.invalid_host_job_schedule_cron_preview');
  }

  return value;
}

export async function createHostJobSchedule(
  jobDefinitionId: string,
  triggerKind: string,
  timeZoneId: string,
  misfirePolicy: string,
  cronExpression?: string | null,
  oneTimeAtUtc?: string | null,
  startTime?: string | null,
  endTime?: string | null,
  args?: string | null,
  signal?: AbortSignal
): Promise<HostJobSchedule> {
  const value = await jobsCreateHostJobSchedule(
    http,
    {
      body: {
        jobDefinitionId,
        triggerKind,
        cronExpression: cronExpression ?? null,
        timeZoneId,
        oneTimeAtUtc: oneTimeAtUtc ?? null,
        misfirePolicy,
        startTime: startTime ?? null,
        endTime: endTime ?? null,
        args: args ?? null
      }
    },
    signal
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('client.invalid_host_job_schedule');
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
  oneTimeAtUtc?: string | null,
  startTime?: string | null,
  endTime?: string | null,
  args?: string | null,
  signal?: AbortSignal
): Promise<HostJobSchedule> {
  const value = await jobsUpdateHostJobSchedule(
    http,
    {
      scheduleId: id,
      body: {
        triggerKind,
        cronExpression: cronExpression ?? null,
        timeZoneId,
        oneTimeAtUtc: oneTimeAtUtc ?? null,
        misfirePolicy,
        startTime: startTime ?? null,
        endTime: endTime ?? null,
        args: args ?? null,
        version
      }
    },
    signal
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('client.invalid_host_job_schedule');
  }

  return value;
}

export async function pauseHostJobSchedule(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostJobSchedule> {
  const value = await jobsPauseHostJobSchedule(
    http,
    {
      scheduleId: id,
      body: { version }
    },
    signal
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('client.invalid_host_job_schedule');
  }

  return value;
}

export async function resumeHostJobSchedule(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostJobSchedule> {
  const value = await jobsResumeHostJobSchedule(
    http,
    {
      scheduleId: id,
      body: { version }
    },
    signal
  );
  if (!isHostJobSchedule(value)) {
    throw new Error('client.invalid_host_job_schedule');
  }

  return value;
}

export async function deleteHostJobSchedule(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await jobsDeleteHostJobSchedule(
    http,
    {
      scheduleId: id,
      body: { version }
    },
    signal
  );
}

export type {
  HostJobSchedule,
  HostJobScheduleCronPreview,
  HostJobScheduleDefinitionOption,
  HostJobSchedulePage
};
