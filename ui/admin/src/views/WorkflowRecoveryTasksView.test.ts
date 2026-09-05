import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import WorkflowRecoveryTasksView from './WorkflowRecoveryTasksView.vue';
import { useSessionStore } from '../auth/session';
import {
  getWorkflowRecoveryTask,
  listWorkflowRecoveryTasks,
  reconcileWorkflowRecoveryTask,
  retryWorkflowRecoveryTask
} from '../api/workflow-recovery-tasks';

vi.mock('../api/workflow-recovery-tasks', () => ({
  getWorkflowRecoveryTask: vi.fn(),
  listWorkflowRecoveryTasks: vi.fn(),
  reconcileWorkflowRecoveryTask: vi.fn(),
  retryWorkflowRecoveryTask: vi.fn()
}));

const listMock = vi.mocked(listWorkflowRecoveryTasks);
const getMock = vi.mocked(getWorkflowRecoveryTask);

function task(statusKey: string, idSuffix = '03') {
  return {
    id: `0198f36e-f7a7-7c52-9cbb-774e674112${idSuffix}`,
    instanceId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
    stepId: null,
    kindKey: 'stuck_instance',
    statusKey,
    attemptCount: statusKey === 'failed' ? 3 : 0,
    revision: 2,
    leaseOwnerKey: statusKey === 'pending' ? 'worker-a' : null,
    leaseExpiresAtUtc: null,
    leaseGeneration: 1,
    nextAttemptAtUtc: null,
    lastError: statusKey === 'failed' ? 'kind=stuck_instance;todo=none' : null,
    createdAtUtc: '2026-09-05T00:00:00Z',
    updatedAtUtc: '2026-09-05T00:00:01Z'
  };
}

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(WorkflowRecoveryTasksView, { global: { plugins: [pinia] } });
}

describe('Vue 工作流恢复任务页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [task('failed'), task('succeeded', '08')],
      page: 1,
      pageSize: 20,
      total: 2
    });
    getMock.mockReset().mockResolvedValue(task('failed'));
    vi.mocked(retryWorkflowRecoveryTask).mockReset().mockResolvedValue(task('pending'));
    vi.mocked(reconcileWorkflowRecoveryTask).mockReset().mockResolvedValue(task('succeeded'));
  });

  it('只读权限不显示重试和对账入口', async () => {
    const wrapper = mountWithPermissions(['workflow.recovery_tasks.read']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-recovery-tasks-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-recovery-tasks-retry"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="workflow-recovery-tasks-reconcile"]').exists()).toBe(false);
  });

  it('failed 且拥有 retry/reconcile 时提交修订号和幂等键', async () => {
    const wrapper = mountWithPermissions([
      'workflow.recovery_tasks.read',
      'workflow.recovery_tasks.retry',
      'workflow.recovery_tasks.reconcile'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-recovery-tasks-load"]').trigger('click');
    await flushPromises();

    await wrapper.get('[data-testid="workflow-recovery-tasks-retry-reason"]').setValue('卡住实例');
    await wrapper.get('[data-testid="workflow-recovery-tasks-retry"]').trigger('click');
    await flushPromises();

    expect(retryWorkflowRecoveryTask).toHaveBeenCalledWith(
      task('failed').id,
      expect.objectContaining({
        expectedRevision: 2,
        reason: '卡住实例',
        idempotencyKey: expect.stringMatching(/^retry-/)
      })
    );

    await wrapper.get('[data-testid="workflow-recovery-tasks-reconcile"]').trigger('click');
    await flushPromises();
    expect(reconcileWorkflowRecoveryTask).toHaveBeenCalledWith(
      task('failed').id,
      expect.objectContaining({
        expectedRevision: 2,
        idempotencyKey: expect.stringMatching(/^reconcile-/)
      })
    );
  });

  it('409 冲突时展示问题详情且不吞掉修订号', async () => {
    vi.mocked(retryWorkflowRecoveryTask).mockRejectedValue({
      status: 409,
      code: 'workflow.revision.conflict',
      title: '修订冲突'
    });
    const wrapper = mountWithPermissions([
      'workflow.recovery_tasks.read',
      'workflow.recovery_tasks.retry'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-recovery-tasks-load"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-recovery-tasks-retry-reason"]').setValue('卡住实例');
    await wrapper.get('[data-testid="workflow-recovery-tasks-retry"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('workflow.revision.conflict');
  });
});
