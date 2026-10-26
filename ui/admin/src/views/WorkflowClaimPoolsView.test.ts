import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { claimWorkflowPool, listWorkflowClaimPools } from '../api/workflow-claims';
import WorkflowClaimPoolsView from './WorkflowClaimPoolsView.vue';

const permissions = new Set<string>();
const session = reactive({ switching: false, currentUser: { id: 'actor', sessionId: 'session', tenantId: null as string | null, scope: 'host' } });
vi.mock('../api/workflow-claims', () => ({ claimWorkflowPool: vi.fn(), listWorkflowClaimPools: vi.fn() }));
vi.mock('../auth/permission', () => ({ usePermission: () => ({ can: (permission: string) => permissions.has(permission) }) }));
vi.mock('../auth/session', () => ({ useSessionStore: () => session }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn().mockResolvedValue(undefined) }) }));
const pool = { id: '01912345-6789-7abc-8def-0123456789ab', instanceId: '01912345-6789-7abc-8def-0123456789ac',
  nodeKey: 'claim', definitionKey: 'claim.one', businessType: 'request', businessId: 'one', businessTitle: '候选任务',
  revision: 1, claimCycle: 0, createdAtUtc: '2026-10-25T00:00:00Z', dueAtUtc: null };
beforeEach(() => {
  vi.clearAllMocks(); permissions.clear(); session.currentUser.tenantId = null; session.switching = false;
  vi.mocked(listWorkflowClaimPools).mockResolvedValue({ items: [pool], totalCount: 1, page: 1, pageSize: 20 });
});
describe('候选领取页面', () => {
  it('缺少领取池读取权限时不发送查询', async () => {
    const wrapper = mount(WorkflowClaimPoolsView);
    try { await flushPromises(); expect(listWorkflowClaimPools).not.toHaveBeenCalled(); expect(claimWorkflowPool).not.toHaveBeenCalled(); }
    finally { wrapper.unmount(); }
  });
  it('读取权限不授予领取能力', async () => {
    permissions.add('workflow.claim_pools.read'); const wrapper = mount(WorkflowClaimPoolsView);
    try { await flushPromises(); expect(wrapper.text()).toContain('候选任务'); expect(wrapper.findAll('button').some(button => button.text() === '领取任务')).toBe(false); }
    finally { wrapper.unmount(); }
  });
  it('网络结果未知时重试保持同一修订和幂等键', async () => {
    permissions.add('workflow.claim_pools.read'); permissions.add('workflow.claim_pools.claim');
    vi.mocked(claimWorkflowPool).mockRejectedValue({ status: 503, code: 'test.unavailable', title: '暂时不可用' });
    const wrapper = mount(WorkflowClaimPoolsView);
    try {
      await flushPromises(); const button = () => wrapper.findAll('button').find(candidate => candidate.text() === '领取任务')!;
      await button().trigger('click'); await flushPromises(); await button().trigger('click'); await flushPromises();
      expect(claimWorkflowPool).toHaveBeenCalledTimes(2);
      expect(vi.mocked(claimWorkflowPool).mock.calls[0]).toEqual(vi.mocked(claimWorkflowPool).mock.calls[1]);
      expect(vi.mocked(claimWorkflowPool).mock.calls[0]?.[1].expectedRevision).toBe(1);
    } finally { wrapper.unmount(); }
  });
});
