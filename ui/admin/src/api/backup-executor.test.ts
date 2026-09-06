import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import {
  getBackupExecutorStatus,
  listBackupRuns,
  listBackupTasks
} from './backup-executor';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));

const requestMock = vi.mocked(request);

const status = {
  artifactRootPath: 'App_Data/backup-artifacts',
  artifactRootExists: false,
  enabledTaskCount: 2,
  deploymentNotice: 'deployment only'
};

const task = {
  id: '01954f00-0001-7000-8000-000000000001',
  taskKey: 'sql-server-full',
  displayName: 'SQL Server 全库备份',
  description: 'desc',
  databaseProvider: 'sql_server',
  isEnabled: true,
  sortOrder: 10,
  createdAtUtc: '2026-09-06T00:00:00.000Z',
  updatedAtUtc: null
};

describe('backup-executor api', () => {
  beforeEach(() => requestMock.mockReset());

  it('reads status, tasks and runs with expected routes', async () => {
    requestMock
      .mockResolvedValueOnce(status)
      .mockResolvedValueOnce([task])
      .mockResolvedValueOnce({
        items: [],
        page: 1,
        pageSize: 20,
        total: 0
      });

    await getBackupExecutorStatus();
    await listBackupTasks();
    await listBackupRuns({ taskId: task.id, status: 'succeeded' });

    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/platform/backup-executor/status',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      '/api/v1/platform/backup-executor/tasks',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/platform/backup-executor/runs?page=1&pageSize=20&taskId=01954f00-0001-7000-8000-000000000001&status=succeeded',
      { method: 'GET' },
      undefined
    );
  });
});
