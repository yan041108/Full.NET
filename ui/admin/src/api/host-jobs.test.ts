import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createHostJobDefinition,
  disableHostJobDefinition,
  listHostJobDefinitions,
  listHostJobExecutions,
  listHostJobGroups,
  triggerHostJobDefinition
} from './host-jobs';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const definition = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  jobKey: 'jobs.ping',
  handlerKind: 'ping',
  args: null,
  displayName: '探针任务',
  description: '烟囱验证',
  groupName: 'System',
  isEnabled: true,
  allowConcurrentExecutions: false,
  createdAtUtc: '2026-07-26T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

const execution = {
  id: '01912345-6789-7abc-8def-0123456789ac',
  jobDefinitionId: definition.id,
  jobScheduleId: null,
  status: 'succeeded',
  triggerKind: 'manual',
  scheduledForUtc: null,
  errorMessage: null,
  startedAtUtc: '2026-07-26T00:00:01Z',
  finishedAtUtc: '2026-07-26T00:00:02Z',
  nextAttemptAtUtc: null,
  attemptCount: 1,
  createdAtUtc: '2026-07-26T00:00:00Z'
};

describe('host-jobs api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists definitions and groups', async () => {
    requestMock
      .mockResolvedValueOnce({
        items: [definition],
        page: 1,
        pageSize: 20,
        total: 1
      })
      .mockResolvedValueOnce([{ groupName: 'System' }]);

    await expect(listHostJobDefinitions(1, 20)).resolves.toMatchObject({ total: 1 });
    await expect(listHostJobGroups()).resolves.toHaveLength(1);
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/jobs/host-definitions?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/jobs/host-definitions/groups',
      { method: 'GET' },
      undefined
    );
  });

  it('creates, disables and triggers jobs', async () => {
    requestMock
      .mockResolvedValueOnce(definition)
      .mockResolvedValueOnce({ ...definition, isEnabled: false, version: 2 })
      .mockResolvedValueOnce(execution);

    await expect(
      createHostJobDefinition('jobs.ping', 'ping', '探针任务')
    ).resolves.toMatchObject({ jobKey: 'jobs.ping' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/jobs/host-definitions',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          jobKey: 'jobs.ping',
          handlerKind: 'ping',
          args: null,
          displayName: '探针任务',
          description: null,
          groupName: null,
          allowConcurrentExecutions: false
        })
      }),
      undefined
    );

    await expect(disableHostJobDefinition(definition.id, 1))
      .resolves.toMatchObject({ isEnabled: false });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/jobs/host-definitions/${definition.id}/disable`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 1 })
      }),
      undefined
    );

    await expect(triggerHostJobDefinition(definition.id))
      .resolves.toMatchObject({ status: 'succeeded' });
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      { method: 'POST' },
      undefined
    );
  });

  it('lists executions with filters', async () => {
    requestMock.mockResolvedValueOnce({
      items: [execution],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listHostJobExecutions({
      jobDefinitionId: definition.id,
      status: 'succeeded'
    })).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/jobs/host-executions?page=1&pageSize=20&jobDefinitionId=${definition.id}&status=succeeded`,
      { method: 'GET' },
      undefined
    );
  });
});
