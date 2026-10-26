import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { ElInput, ElSelect } from 'element-plus';
import WorkflowConnectorCallsPanel from './WorkflowConnectorCallsPanel.vue';
import { listWorkflowConnectorCalls, reconcileWorkflowConnector } from '../api/workflow-connectors';
vi.mock('../api/workflow-connectors', () => ({ listWorkflowConnectorCalls: vi.fn(), reconcileWorkflowConnector: vi.fn() }));
const id = '019a23f0-0000-7000-8000-000000000001'; const other = '019a23f0-0000-7000-8000-000000000002';
const call = { id: other, instanceId: id, nodeKey: 'call', connectorKey: 'credit.check', connectorVersion: 1, modeKey: 'sync', statusKey: 'outcome_unknown',
  revision: 3, attemptCount: 1, applyAttemptCount: 0, errorKey: 'workflow.connector.result_unconfirmed', inputFormRevision: 1, appliedFormRevision: null,
  cancellationRequested: false, requiresReconciliation: true, createdAtUtc: '2026-10-26T00:00:00Z', completedAtUtc: null, attempts: [] };
const props = { instanceId: id, revision: 9, statusKey: 'suspended', canReconcile: true };
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowConnectorCalls).mockResolvedValue({ items: [call], page: 1, pageSize: 20, total: 1 }); });
describe('连接器人工对账', () => {
  it('只读用户只看到运维摘要，禁止构造对账请求', async () => {
    const wrapper = mount(WorkflowConnectorCallsPanel, { props: { ...props, canReconcile: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.find('.connector-call').text()).toContain('credit.check@1');
      expect(wrapper.find('.connector-call button').exists()).toBe(false); expect(wrapper.find('form').exists()).toBe(false); expect(reconcileWorkflowConnector).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it('未知响应重试保留原双修订和同一语义幂等键', async () => {
    vi.mocked(reconcileWorkflowConnector).mockRejectedValueOnce(new Error('receipt unavailable')).mockResolvedValueOnce({ id, callId: other, instanceId: id,
      actionKey: 'retry', statusKey: 'pending', revision: 4, instanceStatusKey: 'suspended', instanceRevision: 10, createdAtUtc: '2026-10-26T00:00:00Z' });
    const wrapper = mount(WorkflowConnectorCallsPanel, { props, global: { plugins: [createPinia()] } });
    try { await flushPromises(); await wrapper.get('.connector-call button').trigger('click'); await flushPromises();
      wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 'retry');
      const inputs = wrapper.findAllComponents(ElInput); inputs[0]!.vm.$emit('update:modelValue', 'ticket:verified'); inputs[1]!.vm.$emit('update:modelValue', 'owner verified'); await flushPromises();
      await wrapper.get('form').trigger('submit'); await flushPromises(); await wrapper.get('form').trigger('submit'); await flushPromises();
      expect(reconcileWorkflowConnector).toHaveBeenCalledTimes(2); const first = vi.mocked(reconcileWorkflowConnector).mock.calls[0]!;
      expect(first[2]).toMatchObject({ expectedInstanceRevision: 9, expectedRevision: 3, actionKey: 'retry', attemptId: null, evidenceReference: 'ticket:verified' });
      expect(vi.mocked(reconcileWorkflowConnector).mock.calls[1]![2]).toEqual(first[2]); expect(wrapper.emitted('resolved')).toHaveLength(1);
    } finally { wrapper.unmount(); }
  });
  it('实例切换丢弃旧作用域迟到读取', async () => {
    let finish!: (value: Awaited<ReturnType<typeof listWorkflowConnectorCalls>>) => void;
    vi.mocked(listWorkflowConnectorCalls).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce({ items: [], page: 1, pageSize: 20, total: 0 });
    const wrapper = mount(WorkflowConnectorCallsPanel, { props, global: { plugins: [createPinia()] } });
    try { await wrapper.setProps({ instanceId: other }); await flushPromises(); finish({ items: [call], page: 1, pageSize: 20, total: 1 }); await flushPromises();
      expect(wrapper.find('.connector-call').exists()).toBe(false); expect(wrapper.find('form').exists()).toBe(false);
    } finally { wrapper.unmount(); }
  });
});
