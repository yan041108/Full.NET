import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowCancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
  workflowReassignInstance
} from '@fullnet/client-contracts';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs,
  reassignWorkflowInstance
} from './workflow-instances';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowCancelInstance: vi.fn(),
  workflowGetInstance: vi.fn(),
  workflowListInstanceExecutionLogs: vi.fn(),
  workflowReassignInstance: vi.fn()
}));

describe('workflow instances api', () => {
  beforeEach(() => {
    vi.mocked(workflowCancelInstance).mockReset();
    vi.mocked(workflowGetInstance).mockReset();
    vi.mocked(workflowListInstanceExecutionLogs).mockReset();
    vi.mocked(workflowReassignInstance).mockReset();
  });

  it('通过生成客户端携带目标用户、实例修订号和幂等键改派待办', async () => {
    const instanceId = '01912345-6789-7abc-8def-0123456789ab';
    const body = {
      assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
      expectedRevision: 3,
      reason: '审批人请假',
      idempotencyKey: 'reassign-01912345'
    };
    vi.mocked(workflowReassignInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789af',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });

    await expect(reassignWorkflowInstance(instanceId, body)).resolves
      .toMatchObject({ statusKey: 'active', revision: 4 });
    expect(workflowReassignInstance).toHaveBeenCalledWith(
      expect.anything(),
      { instanceId, body },
      undefined
    );
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
