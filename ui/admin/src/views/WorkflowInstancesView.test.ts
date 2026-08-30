import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import {
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs
} from '../api/workflow-instances';
import WorkflowInstancesView from './WorkflowInstancesView.vue';

vi.mock('../api/workflow-instances', () => ({
  getWorkflowInstance: vi.fn(),
  listWorkflowInstanceExecutionLogs: vi.fn()
}));

const instanceId = '01912345-6789-7abc-8def-0123456789ab';

function mountView() {
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
    permissions: ['workflow.instances.read'],
    sessionId: '01912345-6789-7abc-8def-0123456789a2',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(WorkflowInstancesView, { global: { plugins: [pinia] } });
}

describe('WorkflowInstancesView', () => {
  beforeEach(() => {
    vi.mocked(getWorkflowInstance).mockReset().mockResolvedValue({
      id: instanceId,
      definitionVersionId: '01912345-6789-7abc-8def-0123456789ac',
      formVersionId: '01912345-6789-7abc-8def-0123456789ad',
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'running',
      revision: 3,
      activeTodoId: '01912345-6789-7abc-8def-0123456789ae',
      startedAtUtc: '2026-08-30T00:00:00Z'
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
    expect(wrapper.get('[data-testid="workflow-instance-summary"]').text()).toContain('running');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')).toHaveLength(2);
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[0]?.text())
      .toContain('instance.started');
    expect(wrapper.findAll('[data-testid="workflow-execution-log"]')[1]?.text())
      .toContain('todo.created');
    expect(wrapper.find('[data-testid="workflow-instance-cancel"]').exists()).toBe(false);
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
});
