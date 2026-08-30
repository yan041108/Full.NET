import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  workflowGetInstance,
  workflowListInstanceExecutionLogs
} from '@fullnet/client-contracts';
import {
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs
} from './workflow-instances';

vi.mock('@fullnet/client-contracts', async importOriginal => ({
  ...await importOriginal<typeof import('@fullnet/client-contracts')>(),
  workflowGetInstance: vi.fn(),
  workflowListInstanceExecutionLogs: vi.fn()
}));

describe('workflow instances api', () => {
  beforeEach(() => {
    vi.mocked(workflowGetInstance).mockReset();
    vi.mocked(workflowListInstanceExecutionLogs).mockReset();
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
