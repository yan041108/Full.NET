import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ElInput } from 'element-plus';
import { getWorkflowRouteDecision, reconcileWorkflowRoute } from '../api/workflow-routes';
import WorkflowRoutesPanel from './WorkflowRoutesPanel.vue';
vi.mock('../api/workflow-routes', () => ({ getWorkflowRouteDecision: vi.fn(), reconcileWorkflowRoute: vi.fn() }));
const root = '11111111-1111-4111-8111-111111111111'; const route = '22222222-2222-4222-8222-222222222222';
const work = '33333333-3333-4333-8333-333333333333';
const row = { id: route, instanceId: root, nodeKey: 'route', statusKey: 'decision_unknown', revision: 1, instanceRevision: 9, instanceStatusKey: 'suspended',
  sourceExecutionEpochId: root, targetExecutionEpochId: null, selectedTargetKey: null, nextNodeKey: null, continuationWorkId: null, normalNextNodeKey: 'end',
  targets: [{ targetKey: 'retry', nodeKey: 'review', regionKey: 'round', reenters: true, maximumIterations: 3 }], createdAtUtc: '2026-10-27T00:00:00Z', appliedAtUtc: null };
const props = { instanceId: root, routeId: route, revision: 9, canReconcile: true };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getWorkflowRouteDecision).mockResolvedValue(row); });
describe('路由恢复', () => {
  it('不确定失败重试复用原目标、双修订及幂等键', async () => {
    vi.mocked(reconcileWorkflowRoute).mockRejectedValueOnce(new Error('lost response')).mockResolvedValueOnce({ routeId: route, continuationWorkId: work, executionEpochId: root, nextNodeKey: 'end' });
    const wrapper = mount(WorkflowRoutesPanel, { props, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); wrapper.getComponent(ElInput).vm.$emit('update:modelValue', '人工核对'); await flushPromises();
      await wrapper.get('form').trigger('submit'); await flushPromises(); await wrapper.get('form').trigger('submit'); await flushPromises();
      const calls = vi.mocked(reconcileWorkflowRoute).mock.calls; expect(calls).toHaveLength(2); expect(calls[1]![2]).toEqual(calls[0]![2]);
      expect(calls[0]![2]).toMatchObject({ expectedInstanceRevision: 9, expectedRevision: 1, selectedTargetKey: null, reason: '人工核对' });
      expect(wrapper.emitted('resolved')).toHaveLength(1);
    } finally { wrapper.unmount(); }
  });
  it('只读权限不显示恢复表单', async () => {
    const wrapper = mount(WorkflowRoutesPanel, { props: { ...props, canReconcile: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.find('form').exists()).toBe(false); expect(reconcileWorkflowRoute).not.toHaveBeenCalled(); }
    finally { wrapper.unmount(); }
  });
  it('切换路由后拒绝旧请求的迟到结果', async () => {
    let finish!: (value: typeof row) => void;
    vi.mocked(getWorkflowRouteDecision).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockRejectedValueOnce(new Error('not found'));
    const wrapper = mount(WorkflowRoutesPanel, { props, global: { plugins: [createPinia()] } });
    try { await wrapper.setProps({ routeId: work }); await flushPromises(); finish(row); await flushPromises(); expect(wrapper.find('form').exists()).toBe(false); }
    finally { wrapper.unmount(); }
  });
});
