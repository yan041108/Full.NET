import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowGetRecoveryTask,
  workflowListRecoveryTasks,
  workflowReconcileRecoveryTask,
  workflowRetryRecoveryTask
} from '@fullnet/client-contracts';
import {
  getWorkflowRecoveryTask,
  listWorkflowRecoveryTasks,
  reconcileWorkflowRecoveryTask,
  retryWorkflowRecoveryTask
} from './workflow-recovery-tasks';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowGetRecoveryTask: vi.fn(),
  workflowListRecoveryTasks: vi.fn(),
  workflowReconcileRecoveryTask: vi.fn(),
  workflowRetryRecoveryTask: vi.fn()
}));

describe('workflow recovery tasks api', () => {
  beforeEach(() => {
    vi.mocked(workflowGetRecoveryTask).mockReset();
    vi.mocked(workflowListRecoveryTasks).mockReset();
    vi.mocked(workflowReconcileRecoveryTask).mockReset();
    vi.mocked(workflowRetryRecoveryTask).mockReset();
  });

  it('通过生成客户端分页查询恢复任务', async () => {
    vi.mocked(workflowListRecoveryTasks).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0
    });

    await expect(listWorkflowRecoveryTasks(1, 20)).resolves.toMatchObject({ page: 1, total: 0 });
    expect(workflowListRecoveryTasks).toHaveBeenCalledWith(
      expect.anything(),
      { page: 1, pageSize: 20 },
      undefined
    );
  });

  it('重试和对账必须显式携带修订号与幂等键', async () => {
    const taskId = '01912345-6789-7abc-8def-0123456789ab';
    const task = {
      id: taskId,
      instanceId: '01912345-6789-7abc-8def-0123456789ac',
      stepId: null,
      kindKey: 'stuck_instance',
      statusKey: 'pending',
      attemptCount: 0,
      revision: 3,
      leaseOwnerKey: null,
      leaseExpiresAtUtc: null,
      leaseGeneration: 0,
      nextAttemptAtUtc: null,
      lastError: null,
      createdAtUtc: '2026-09-05T00:00:00Z',
      updatedAtUtc: '2026-09-05T00:00:00Z'
    };
    vi.mocked(workflowGetRecoveryTask).mockResolvedValue(task);
    vi.mocked(workflowRetryRecoveryTask).mockResolvedValue(task);
    vi.mocked(workflowReconcileRecoveryTask).mockResolvedValue({ ...task, statusKey: 'succeeded' });

    await expect(getWorkflowRecoveryTask(taskId)).resolves.toMatchObject({ id: taskId });
    await retryWorkflowRecoveryTask(taskId, {
      expectedRevision: 2,
      reason: '卡住',
      idempotencyKey: 'retry-1'
    });
    await reconcileWorkflowRecoveryTask(taskId, {
      expectedRevision: 3,
      reason: null,
      idempotencyKey: 'rec-1'
    });

    expect(workflowRetryRecoveryTask).toHaveBeenCalledWith(
      expect.anything(),
      { taskId, body: { expectedRevision: 2, reason: '卡住', idempotencyKey: 'retry-1' } },
      undefined
    );
    expect(workflowReconcileRecoveryTask).toHaveBeenCalledWith(
      expect.anything(),
      { taskId, body: { expectedRevision: 3, reason: null, idempotencyKey: 'rec-1' } },
      undefined
    );
  });
});
