import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createHostJobSchedule,
  listHostJobScheduleDefinitionOptions,
  listHostJobSchedules,
  pauseHostJobSchedule,
  previewHostJobScheduleCron,
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
  jobDefinitionJobKey: 'jobs.ping',
  jobDefinitionDisplayName: 'Ping',
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

  it('lists schedules with filters and pagination', async () => {
    requestMock.mockResolvedValue({
      items: [schedule],
      page: 1,
      pageSize: 20,
      total: 1
    });

    const page = await listHostJobSchedules({
      page: 2,
      pageSize: 10,
      jobDefinitionId: schedule.jobDefinitionId,
      search: 'ping',
      isEnabled: true,
      triggerKind: 'cron'
    });
    expect(page.items).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/jobs/host-schedules?page=2&pageSize=10'
      + `&jobDefinitionId=${encodeURIComponent(schedule.jobDefinitionId)}`
      + '&search=ping&isEnabled=true&triggerKind=cron'
    );
  });

  it('loads definition options and cron preview', async () => {
    requestMock
      .mockResolvedValueOnce([
        {
          id: schedule.jobDefinitionId,
          jobKey: 'jobs.ping',
          displayName: 'Ping'
        }
      ])
      .mockResolvedValueOnce({
        humanDescription: 'jobs.cron.custom',
        nextExecutionAtUtc: '2026-08-03T09:00:00Z',
        nextOccurrencesUtc: ['2026-08-03T09:00:00Z']
      });

    const options = await listHostJobScheduleDefinitionOptions();
    const preview = await previewHostJobScheduleCron('0 9 * * *', 'UTC');

    expect(options).toHaveLength(1);
    expect(preview.nextExecutionAtUtc).toBe('2026-08-03T09:00:00Z');
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/jobs/host-schedules/definition-options'
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      expect.stringContaining('/api/v1/jobs/host-schedules/cron-preview?')
    );
    expect(requestMock.mock.calls[1][0]).toContain('cronExpression=');
    expect(requestMock.mock.calls[1][0]).toContain('timeZoneId=UTC');
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
