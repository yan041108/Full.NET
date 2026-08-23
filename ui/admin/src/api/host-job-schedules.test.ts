import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostJobSchedule,
  listHostJobScheduleDefinitionOptions,
  listHostJobSchedules,
  pauseHostJobSchedule,
  previewHostJobScheduleCron
} from './host-job-schedules';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const definitionId = '01912345-6789-7abc-8def-0123456789ab';

const schedule = {
  id: '01912345-6789-7abc-8def-0123456789ad',
  jobDefinitionId: definitionId,
  jobDefinitionJobKey: 'jobs.ping',
  jobDefinitionDisplayName: '探针任务',
  triggerKind: 'cron',
  cronExpression: '0 9 * * *',
  timeZoneId: 'UTC',
  oneTimeAtUtc: null,
  misfirePolicy: 'skip',
  isEnabled: true,
  nextExecutionAtUtc: '2026-08-03T09:00:00Z',
  lastExecutionAtUtc: null,
  completedAtUtc: null,
  numberOfRuns: 0,
  numberOfErrors: 0,
  startTime: null,
  endTime: null,
  args: null,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('host-job-schedules api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists schedules and definition options', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [schedule],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce([{
        id: definitionId,
        jobKey: 'jobs.ping',
        handlerKind: 'ping',
        displayName: '探针任务'
      }]);

    await expect(listHostJobSchedules({ jobDefinitionId: definitionId }))
      .resolves.toMatchObject({ total: 1 });
    await expect(listHostJobScheduleDefinitionOptions()).resolves.toHaveLength(1);
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/jobs/host-schedules?page=1&pageSize=20&jobDefinitionId=${definitionId}`,
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/jobs/host-schedules/definition-options',
      { method: 'GET' },
      undefined
    );
  });

  it('previews cron and creates a schedule', async () => {
    requestMock
      .mockResolvedValueOnce({
        humanDescription: 'jobs.cron.macro.daily',
        nextExecutionAtUtc: '2026-08-03T09:00:00Z',
        nextOccurrencesUtc: ['2026-08-03T09:00:00Z']
      })
      .mockResolvedValueOnce(schedule);

    await expect(previewHostJobScheduleCron('0 9 * * *', 'UTC'))
      .resolves.toMatchObject({ humanDescription: 'jobs.cron.macro.daily' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/jobs/host-schedules/cron-preview?cronExpression=0+9+*+*+*&timeZoneId=UTC',
      { method: 'GET' },
      undefined
    );

    await expect(
      createHostJobSchedule(
        definitionId,
        'cron',
        'UTC',
        'skip',
        '0 9 * * *'
      )
    ).resolves.toMatchObject({ triggerKind: 'cron' });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/jobs/host-schedules',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          jobDefinitionId: definitionId,
          triggerKind: 'cron',
          cronExpression: '0 9 * * *',
          timeZoneId: 'UTC',
          oneTimeAtUtc: null,
          misfirePolicy: 'skip',
          startTime: null,
          endTime: null,
          args: null
        })
      }),
      undefined
    );
  });

  it('pauses a schedule with optimistic concurrency', async () => {
    requestMock.mockResolvedValueOnce({ ...schedule, isEnabled: false, version: 2 });

    await expect(pauseHostJobSchedule(schedule.id, 1))
      .resolves.toMatchObject({ isEnabled: false });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/jobs/host-schedules/${schedule.id}/pause`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 1 })
      }),
      undefined
    );
  });
});
