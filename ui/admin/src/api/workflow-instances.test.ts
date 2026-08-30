import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowCancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs
} from '@fullnet/client-contracts';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs
} from './workflow-instances';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowCancelInstance: vi.fn(),
  workflowGetInstance: vi.fn(),
  workflowListInstanceExecutionLogs: vi.fn()
}));

describe('workflow instances api', () => {
  beforeEach(() => {
    vi.mocked(workflowCancelInstance).mockReset();
    vi.mocked(workflowGetInstance).mockReset();
    vi.mocked(workflowListInstanceExecutionLogs).mockReset();
  });

  it('通过生成客户端携带实例修订号和幂等键取消实例', async () => {
    const instanceId = '01912345-6789-7abc-8def-0123456789ab';
    const body = {
      expectedRevision: 3,
      reason: null,
      idempotencyKey: 'cancel-01912345'
    };
    vi.mocked(workflowCancelInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'cancelled',
      revision: 4,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });

    await expect(cancelWorkflowInstance(instanceId, body)).resolves
      .toMatchObject({ statusKey: 'cancelled' });
    expect(workflowCancelInstance).toHaveBeenCalledWith(
      expect.anything(),
      { instanceId, body },
      undefined
    );
  });

  it('通过生成客户端查询同一个实例的详情与执行日志', async () => {
    const instanceId = '01912345-6789-7abc-8def-0123456789ab';
    vi.mocked(workflowGetInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'running',
      revision: 3,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(workflowListInstanceExecutionLogs).mockResolvedValue([]);

    await expect(getWorkflowInstance(instanceId)).resolves.toMatchObject({ id: instanceId });
    await expect(listWorkflowInstanceExecutionLogs(instanceId)).resolves.toEqual([]);
    expect(workflowGetInstance).toHaveBeenCalledWith(expect.anything(), { instanceId }, undefined);
    expect(workflowListInstanceExecutionLogs).toHaveBeenCalledWith(
      expect.anything(),
      { instanceId },
      undefined
    );
  });
});
