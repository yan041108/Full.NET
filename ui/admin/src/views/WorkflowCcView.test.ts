import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import { listMyWorkflowCc, markWorkflowCcRead } from '../api/workflow-cc';
import WorkflowCcView from './WorkflowCcView.vue';

vi.mock('../api/workflow-cc', () => ({
  listMyWorkflowCc: vi.fn(),
  markWorkflowCcRead: vi.fn()
}));

const record = {
  id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
  instanceId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002',
  stepId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003',
  nodeKey: 'copy',
  businessType: 'leave.request',
  businessId: 'REQ-001',
  createdAtUtc: '2026-09-04T00:00:00Z',
  readAtUtc: null
};

describe('WorkflowCcView', () => {
  beforeEach(() => {
    vi.mocked(listMyWorkflowCc).mockReset().mockResolvedValue([record]);
    vi.mocked(markWorkflowCcRead).mockReset().mockResolvedValue({
      id: record.id,
      readAtUtc: '2026-09-04T00:01:00Z'
    });
  });

  it('展示本人未读抄送并按精确权限标记已读', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    useSessionStore().currentUser = {
      id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d004',
      username: 'recipient',
      displayName: '抄送人',
      tenantId: null,
      actorScope: 'host',
      scope: 'host',
      isSuperAdministrator: false,
      permissions: ['workflow.cc.read', 'workflow.cc.mark_read'],
      sessionId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d005',
      preferredLocale: 'zh-CN',
      profileVersion: 1
    };
    const wrapper = mount(WorkflowCcView, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(wrapper.text()).toContain('REQ-001');
    await wrapper.get('[data-testid="workflow-cc-mark-read"]').trigger('click');
    await flushPromises();
    expect(markWorkflowCcRead).toHaveBeenCalledWith(record.id);
  });
});
