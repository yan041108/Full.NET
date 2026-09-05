import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DataApprovalScenariosView from './DataApprovalScenariosView.vue';
import { useSessionStore } from '../auth/session';
import {
  listDataApprovalScenarios,
  updateDataApprovalScenarioBinding
} from '../api/data-approval-scenarios';
import { listWorkflowDefinitions, listWorkflowDefinitionVersions } from '../api/workflow-runtime';

vi.mock('../api/data-approval-scenarios', () => ({
  listDataApprovalScenarios: vi.fn(),
  updateDataApprovalScenarioBinding: vi.fn()
}));

vi.mock('../api/workflow-runtime', () => ({
  listWorkflowDefinitions: vi.fn(),
  listWorkflowDefinitionVersions: vi.fn()
}));

const scenario = {
  scenarioKey: 'serial_numbers.host_rule.update',
  scopeKey: 'host',
  isRegistered: true,
  isEnabled: false,
  workflowDefinitionKey: null,
  workflowDefinitionVersionId: null,
  version: null
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
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(DataApprovalScenariosView, { global: { plugins: [pinia] } });
}

describe('Vue 数据审批场景页', () => {
  beforeEach(() => {
    vi.mocked(listDataApprovalScenarios).mockReset().mockResolvedValue([scenario]);
    vi.mocked(listWorkflowDefinitions).mockReset().mockResolvedValue([]);
    vi.mocked(listWorkflowDefinitionVersions).mockReset().mockResolvedValue([]);
    vi.mocked(updateDataApprovalScenarioBinding).mockReset();
  });

  it('read-only 不显示保存按钮', async () => {
    const wrapper = mountWithPermissions(['data_approvals.scenarios.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="data-approval-scenario-save"]').exists()).toBe(false);
    expect(wrapper.get('[data-testid="data-approval-scenario-status"]').text()).toContain('未启用');
  });

  it('manage 权限显示保存按钮', async () => {
    const wrapper = mountWithPermissions([
      'data_approvals.scenarios.read',
      'data_approvals.scenarios.manage'
    ]);
    await flushPromises();
    expect(wrapper.find('[data-testid="data-approval-scenario-save"]').exists()).toBe(true);
  });
});
