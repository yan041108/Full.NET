import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox } from 'element-plus';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs,
  pauseWorkflowInstance,
  recoverWorkflowInstance,
  resumeWorkflowInstance
} from '../api/workflow-instances';
import WorkflowInstancesView from './WorkflowInstancesView.vue';

vi.mock('../api/workflow-instances', () => ({
  cancelWorkflowInstance: vi.fn(),
  getWorkflowInstance: vi.fn(),
  listWorkflowInstanceExecutionLogs: vi.fn(),
  pauseWorkflowInstance: vi.fn(),
  recoverWorkflowInstance: vi.fn(),
  resumeWorkflowInstance: vi.fn()
}));

const instanceId = '01912345-6789-7abc-8def-0123456789ab';

function mountView(permissions = ['workflow.instances.read']) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '01912345-6789-7abc-8def-0123456789aa',
    username: 'reader',
    displayName: '流程观察员',
    tenantId: '01912345-6789-7abc-8def-0123456789a1',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    permissions,
    sessionId: '01912345-6789-7abc-8def-0123456789a2',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(WorkflowInstancesView, { global: { plugins: [pinia] } });
}

describe('WorkflowInstancesView', () => {
  beforeEach(() => {
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(undefined as never);
    vi.mocked(cancelWorkflowInstance).mockReset().mockResolvedValue({
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
    vi.mocked(getWorkflowInstance).mockReset().mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 3,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z',
      dueAtUtc: '2026-08-30T00:30:00Z',
      timeoutStatusKey: 'overdue',
      reminderCount: 2,
      escalatedAtUtc: null
    });
    vi.mocked(listWorkflowInstanceExecutionLogs).mockReset().mockResolvedValue([
      {
        id: '01912345-6789-7abc-8def-0123456789b1',
        instanceId,
        stepId: null,
        transitionKey: 'instance.started',
        fromStatusKey: null,
        toStatusKey: 'running',
        createdAtUtc: '2026-08-30T00:00:00Z'
      },
      {
        id: '01912345-6789-7abc-8def-0123456789b2',
        instanceId,
        stepId: '01912345-6789-7abc-8def-0123456789af',
        transitionKey: 'todo.created',
        fromStatusKey: 'running',
        toStatusKey: 'waiting',
        createdAtUtc: '2026-08-30T00:01:00Z'
      }
    ]);
    vi.mocked(pauseWorkflowInstance).mockReset();
    vi.mocked(resumeWorkflowInstance).mockReset();
    vi.mocked(recoverWorkflowInstance).mockReset();
  });

  it('仅向具有独立取消权限的用户展示并执行活动实例取消', async () => {
    const reader = mountView();
    await reader.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await reader.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(reader.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);

    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.cancel'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-cancel"]').trigger('click');
    await flushPromises();

    expect(cancelWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 3,
        reason: null,
        idempotencyKey: expect.any(String)
      })
    );
    expect(operator.get('[data-testid="workflow-instance-summary"]').text())
      .toContain('cancelled');
    expect(operator.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);
  });

  it('按实例标识同时加载只读概要与顺序执行轨迹', async () => {
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();

    expect(getWorkflowInstance).toHaveBeenCalledWith(instanceId, expect.any(AbortSignal));
    expect(listWorkflowInstanceExecutionLogs).toHaveBeenCalledWith(
      instanceId,
      expect.any(AbortSignal)
    );
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('PO-001');
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('active');
    expect(wrapper.get('[data-testid="workflow-instance-timeout-status"]').text())
      .toContain('已逾期');
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('2');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(2);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[0]?.text())
      .toContain('instance.started');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[1]?.text())
      .toContain('todo.created');
    expect(wrapper.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-pause"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);
  });

  it('查询失败时清除旧实例并展示 ProblemDetails', async () => {
    const wrapper = mountView();
    await wrapper.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    vi.mocked(getWorkflowInstance).mockRejectedValueOnce({
      status: 403,
      code: 'authorization.permission_denied',
      title: 'Forbidden',
      traceId: 'trace-workflow-instance'
    });

    await wrapper.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-instance-summary"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(0);
    expect(wrapper.get('[role="alert"]').text()).toContain('authorization.permission_denied');
    expect(wrapper.get('[role="alert"]').text()).toContain('trace-workflow-instance');
  });

  it('仅向具有暂停权限的用户展示活动实例暂停入口并提交修订号与新幂等键', async () => {
    vi.mocked(pauseWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'suspended',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.pause'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-pause"]').trigger('click');
    await flushPromises();

    expect(pauseWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 3,
        reason: null,
        idempotencyKey: expect.stringMatching(/^pause-/)
      })
    );
    expect(listWorkflowInstanceExecutionLogs).toHaveBeenCalledTimes(2);
    expect(operator.get('[data-testid="workflow-instance-summary"]').text())
      .toContain('suspended');
    expect(operator.find('[data-testid="workflow-instance-pause"]').exists()).toBe(false);
  });

  it('暂停实例仅向 resume 与 recover 权限分别创建恢复入口，强制恢复必须提交原因', async () => {
    vi.mocked(getWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'suspended',
      revision: 4,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(resumeWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 5,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.mocked(recoverWorkflowInstance).mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'active',
      revision: 5,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    vi.spyOn(ElMessageBox, 'prompt').mockResolvedValue({ value: '卡住后强制恢复' } as never);

    const reader = mountView();
    await reader.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await reader.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(reader.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    expect(reader.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);

    const resumer = mountView([
      'workflow.instances.read',
      'workflow.instances.resume'
    ]);
    await resumer.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await resumer.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(resumer.find('[data-testid="workflow-instance-recover"]').exists()).toBe(false);
    await resumer.get('[data-testid="workflow-instance-resume"]').trigger('click');
    await flushPromises();
    expect(resumeWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 4,
        idempotencyKey: expect.stringMatching(/^resume-/)
      })
    );

    const recoverer = mountView([
      'workflow.instances.read',
      'workflow.instances.recover'
    ]);
    await recoverer.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await recoverer.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    expect(recoverer.find('[data-testid="workflow-instance-resume"]').exists()).toBe(false);
    await recoverer.get('[data-testid="workflow-instance-recover"]').trigger('click');
    await flushPromises();
    expect(recoverWorkflowInstance).toHaveBeenCalledWith(
      instanceId,
      expect.objectContaining({
        expectedRevision: 4,
        reason: '卡住后强制恢复',
        idempotencyKey: expect.stringMatching(/^recover-/)
      })
    );
  });

  it('409 冲突展示 ProblemDetails 并提供刷新入口', async () => {
    vi.mocked(pauseWorkflowInstance).mockRejectedValueOnce({
      status: 409,
      code: 'workflow.revision.conflict',
      title: 'The workflow instance was updated by another request.',
      traceId: 'trace-workflow-conflict'
    });
    const operator = mountView([
      'workflow.instances.read',
      'workflow.instances.pause'
    ]);
    await operator.get('[data-testid="workflow-instance-id"]').setValue(instanceId);
    await operator.get('[data-testid="workflow-instance-search"]').trigger('click');
    await flushPromises();
    await operator.get('[data-testid="workflow-instance-pause"]').trigger('click');
    await flushPromises();

    expect(operator.get('[role="alert"]').text()).toContain('workflow.revision.conflict');
    expect(operator.find('[data-testid="workflow-instance-conflict-refresh"]').exists()).toBe(true);
    await operator.get('[data-testid="workflow-instance-conflict-refresh"]').trigger('click');
    await flushPromises();
    expect(getWorkflowInstance).toHaveBeenCalledTimes(2);
  });
});
