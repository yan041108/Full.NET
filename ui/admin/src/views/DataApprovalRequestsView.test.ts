import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DataApprovalRequestsView from './DataApprovalRequestsView.vue';
import { useSessionStore } from '../auth/session';
import {
  cancelDataApprovalRequest,
  createDataApprovalRequest,
  getDataApprovalRequest,
  listDataApprovalRequests,
  retryDataApprovalRequest,
  retryDataApprovalApplyRequest
} from '../api/data-approval-requests';
import { listDataApprovalScenarios } from '../api/data-approval-scenarios';

vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {}
  }),
  useRouter: () => ({
    push: vi.fn()
  })
}));

vi.mock('../api/data-approval-requests', () => ({
  listDataApprovalRequests: vi.fn(),
  getDataApprovalRequest: vi.fn(),
  createDataApprovalRequest: vi.fn(),
  cancelDataApprovalRequest: vi.fn(),
  retryDataApprovalRequest: vi.fn(),
  retryDataApprovalApplyRequest: vi.fn()
}));

vi.mock('../api/data-approval-scenarios', () => ({
  listDataApprovalScenarios: vi.fn()
}));

const listMock = vi.mocked(listDataApprovalRequests);
const getMock = vi.mocked(getDataApprovalRequest);
const request = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  scenarioKey: 'serial_numbers.host_rule.update',
  targetEntityId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  statusKey: 'in_review',
  beforeSnapshotJson: '{"displayName":"Old"}',
  afterSnapshotJson: '{"displayName":"New"}',
  workflowInstanceId: '0198f36e-f7a7-7c52-9cbb-774e67411206',
  workflowRevision: 1,
  workflowDefinitionVersionId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
  submittedByUserId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
  submittedAtUtc: '2026-09-05T08:00:00Z',
  resolvedAtUtc: null,
  recoveryStatusKey: 'none',
  lastFailureCode: null,
  lastFailureMessage: null,
  lastRecoveryAttemptAtUtc: null,
  recoveryAttemptCount: 0,
  applicationStatusKey: 'none',
  lastApplicationFailureCode: null,
  lastApplicationFailureMessage: null,
  lastApplicationAttemptAtUtc: null,
  applicationAttemptCount: 0,
  version: 2
};

const recoverableRequest = {
  ...request,
  statusKey: 'pending',
  workflowInstanceId: null,
  workflowRevision: null,
  recoveryStatusKey: 'failed_retryable',
  lastFailureCode: 'workflow.start_failed',
  lastFailureMessage: 'start failed',
  recoveryAttemptCount: 1
};

const applyFailedRequest = {
  ...request,
  statusKey: 'in_review',
  applicationStatusKey: 'failed_retryable',
  lastApplicationFailureCode: 'serial_numbers.rule.version_conflict',
  lastApplicationFailureMessage: 'conflict',
  applicationAttemptCount: 1
};

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
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(DataApprovalRequestsView, { global: { plugins: [pinia] } });
}

describe('Vue 数据审批请求页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({ items: [request], page: 1, pageSize: 20, total: 1 });
    getMock.mockReset().mockResolvedValue(request);
    vi.mocked(listDataApprovalScenarios).mockReset().mockResolvedValue([{
      scenarioKey: 'serial_numbers.host_rule.update',
      scopeKey: 'host',
      isRegistered: true,
      isEnabled: true,
      workflowDefinitionKey: 'serial-rule-update',
      workflowDefinitionVersionId: '0198f36e-f7a7-7c52-9cbb-774e67411207',
      version: 1
    }]);
    vi.mocked(createDataApprovalRequest).mockReset();
    vi.mocked(cancelDataApprovalRequest).mockReset();
    vi.mocked(retryDataApprovalRequest).mockReset();
    vi.mocked(retryDataApprovalApplyRequest).mockReset();
  });

  it('仅有 read 时不显示创建、取消、重试关联与重试应用操作', async () => {
    const wrapper = mountWithPermissions(['data_approvals.requests.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="data-approval-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="data-approval-cancel"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="data-approval-retry"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="data-approval-retry-apply"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="data-approval-load"]').exists()).toBe(true);
  });

  it('列表展示稳定状态键', async () => {
    const wrapper = mountWithPermissions(['data_approvals.requests.read']);
    await flushPromises();
    expect(wrapper.get('[data-testid="data-approval-status"]').text()).toBe('in_review');
  });

  it('create-only 显示提交按钮', async () => {
    const wrapper = mountWithPermissions(['data_approvals.requests.read', 'data_approvals.requests.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="data-approval-create"]').exists()).toBe(true);
  });

  it('详情加载后展示 before/after 快照与工作流实例', async () => {
    const wrapper = mountWithPermissions(['data_approvals.requests.read']);
    await flushPromises();
    await wrapper.get('[data-testid="data-approval-load"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[data-testid="data-approval-detail-status"]').text()).toContain('in_review');
    expect(wrapper.get('[data-testid="data-approval-detail-workflow"]').text())
      .toContain('0198f36e-f7a7-7c52-9cbb-774e67411206');
    expect(wrapper.get('[data-testid="data-approval-before"]').text()).toContain('Old');
    expect(wrapper.get('[data-testid="data-approval-after"]').text()).toContain('New');
  });

  it('可恢复失败时展示恢复状态与重试按钮', async () => {
    getMock.mockResolvedValue(recoverableRequest);
    const wrapper = mountWithPermissions([
      'data_approvals.requests.read',
      'data_approvals.requests.retry'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="data-approval-load"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[data-testid="data-approval-detail-recovery"]').text()).toContain('可重试失败');
    expect(wrapper.get('[data-testid="data-approval-detail-failure"]').text()).toContain('workflow.start_failed');
    expect(wrapper.find('[data-testid="data-approval-retry"]').exists()).toBe(true);
  });

  it('应用失败时展示应用状态与重试应用按钮', async () => {
    getMock.mockResolvedValue(applyFailedRequest);
    const wrapper = mountWithPermissions([
      'data_approvals.requests.read',
      'data_approvals.requests.retry_apply'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="data-approval-load"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[data-testid="data-approval-detail-application"]').text()).toContain('可重试应用失败');
    expect(wrapper.get('[data-testid="data-approval-detail-application-failure"]').text())
      .toContain('serial_numbers.rule.version_conflict');
    expect(wrapper.find('[data-testid="data-approval-retry-apply"]').exists()).toBe(true);
  });
});
