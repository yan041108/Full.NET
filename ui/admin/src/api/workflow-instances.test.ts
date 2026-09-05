import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowCancelInstance,
  workflowGetInstance,
  workflowListInstanceExecutionLogs,
  workflowPauseInstance,
  workflowRecoverInstance,
  workflowReassignInstance,
  workflowResumeInstance
} from '@fullnet/client-contracts';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs,
  pauseWorkflowInstance,
  recoverWorkflowInstance,
  reassignWorkflowInstance,
  resumeWorkflowInstance
} from './workflow-instances';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowCancelInstance: vi.fn(),
  workflowGetInstance: vi.fn(),
  workflowListInstanceExecutionLogs: vi.fn(),
  workflowPauseInstance: vi.fn(),
  workflowRecoverInstance: vi.fn(),
  workflowReassignInstance: vi.fn(),
  workflowResumeInstance: vi.fn()
}));

describe('workflow instances api', () => {
  beforeEach(() => {
    vi.mocked(workflowCancelInstance).mockReset();
    vi.mocked(workflowGetInstance).mockReset();
    vi.mocked(workflowListInstanceExecutionLogs).mockReset();
    vi.mocked(workflowPauseInstance).mockReset();
    vi.mocked(workflowRecoverInstance).mockReset();
    vi.mocked(workflowReassignInstance).mockReset();
    vi.mocked(workflowResumeInstance).mockReset();
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

  it('通过生成客户端携带实例修订号和幂等键暂停、恢复与强制恢复', async () => {
    const instanceId = '01912345-6789-7abc-8def-0123456789ab';
    const snapshot = {
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'suspended',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789af',
      startedAtUtc: '2026-08-30T00:00:00Z'
    };
    vi.mocked(workflowPauseInstance).mockResolvedValue(snapshot);
    vi.mocked(workflowResumeInstance).mockResolvedValue({ ...snapshot, statusKey: 'active', revision: 5 });
    vi.mocked(workflowRecoverInstance).mockResolvedValue({ ...snapshot, statusKey: 'active', revision: 5 });

    await expect(pauseWorkflowInstance(instanceId, {
      expectedRevision: 3,
      reason: null,
      idempotencyKey: 'pause-01912345'
    })).resolves.toMatchObject({ statusKey: 'suspended' });
    await expect(resumeWorkflowInstance(instanceId, {
      expectedRevision: 4,
      reason: null,
      idempotencyKey: 'resume-01912345'
    })).resolves.toMatchObject({ statusKey: 'active' });
    await expect(recoverWorkflowInstance(instanceId, {
      expectedRevision: 4,
      reason: '卡住后强制恢复',
      idempotencyKey: 'recover-01912345'
    })).resolves.toMatchObject({ revision: 5 });
    expect(workflowPauseInstance).toHaveBeenCalledWith(
      expect.anything(),
      { instanceId, body: expect.objectContaining({ expectedRevision: 3 }) },
      undefined
    );
    expect(workflowResumeInstance).toHaveBeenCalledWith(
      expect.anything(),
      { instanceId, body: expect.objectContaining({ expectedRevision: 4 }) },
      undefined
    );
    expect(workflowRecoverInstance).toHaveBeenCalledWith(
      expect.anything(),
      {
        instanceId,
        body: expect.objectContaining({
          expectedRevision: 4,
          reason: '卡住后强制恢复'
        })
      },
      undefined
    );
  });
});
