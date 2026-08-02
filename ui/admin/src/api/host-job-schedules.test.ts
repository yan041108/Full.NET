import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createHostJobSchedule,
  listHostJobSchedules,
  pauseHostJobSchedule,
  resumeHostJobSchedule,
  updateHostJobSchedule
} from './host-job-schedules';

const requestMock = vi.hoisted(() => vi.fn());

vi.mock('./http', () => ({
  request: requestMock
}));

const schedule = {
  id: '01912345-6789-7abc-8def-0123456789ad',
  jobDefinitionId: '01912345-6789-7abc-8def-0123456789ab',
  triggerKind: 'cron',
  cronExpression: '0 9 * * *',
  timeZoneId: 'UTC',
  oneTimeAtUtc: null,
  misfirePolicy: 'skip',
  isEnabled: true,
  nextExecutionAtUtc: '2026-08-03T09:00:00Z',
  lastExecutionAtUtc: null,
  completedAtUtc: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('host-job-schedules api', () => {
  beforeEach(() => {
    requestMock.mockReset();
  });

  it('lists schedules with optional definition filter', async () => {
    requestMock.mockResolvedValue({
      items: [schedule],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listHostJobSchedules(schedule.jobDefinitionId);
    expect(page.items).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/jobs/host-schedules?page=1&pageSize=20&jobDefinitionId=${encodeURIComponent(schedule.jobDefinitionId)}`
    );
  });

  it('creates and mutates schedules through validated payloads', async () => {
    requestMock.mockResolvedValue(schedule);

    await createHostJobSchedule(
      schedule.jobDefinitionId,
      'cron',
      'UTC',
      'skip',
      '0 9 * * *'
    );
    await updateHostJobSchedule(
      schedule.id,
      'cron',
      'UTC',
      'skip',
      1,
      '0 10 * * *'
    );
    await pauseHostJobSchedule(schedule.id, 1);
    await resumeHostJobSchedule(schedule.id, 2);

    expect(requestMock).toHaveBeenCalledTimes(4);
  });

  it('rejects invalid schedule payloads', async () => {
    requestMock.mockResolvedValue({ id: 'bad' });
    await expect(listHostJobSchedules()).rejects.toThrow(
      'Invalid host job schedule page response'
    );
  });
});
