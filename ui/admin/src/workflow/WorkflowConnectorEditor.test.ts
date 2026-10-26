import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { ElSelect } from 'element-plus';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import WorkflowConnectorEditor from './WorkflowConnectorEditor.vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
import { listWorkflowConnectors } from '../api/workflow-connectors';
vi.mock('../api/workflow-connectors', () => ({ listWorkflowConnectors: vi.fn() }));
const configuration = { fieldPolicies: { title: 'readOnly', count: 'editable' }, connectorPolicy: { connectorKey: 'credit.check', connectorVersion: 1, inputFields: { subject: 'title' }, outputFields: { count: 'score' } } };
const fields: WorkflowFormField[] = [{ fieldKey: 'title', fieldTypeKey: 'text', required: true, constraints: {} }, { fieldKey: 'count', fieldTypeKey: 'integer', required: false, constraints: {} }];
beforeEach(() => { vi.clearAllMocks(); vi.mocked(listWorkflowConnectors).mockResolvedValue({ items: [{ connectorKey: 'credit.check', connectorVersion: 1, supportsIdempotency: true,
  inputParameters: [{ parameterKey: 'subject', fieldTypeKey: 'text', required: true, constraints: {} }], outputParameters: [{ parameterKey: 'score', fieldTypeKey: 'integer', required: true, constraints: {} }] }] }); });
describe('连接器配置抽屉', () => {
  it('从当前作用域目录回显版本并保存精确映射', async () => {
    const wrapper = mount(WorkflowConnectorEditor, { props: { modelValue: configuration, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.get('[data-testid="connector-save"]').attributes('disabled')).toBeUndefined();
      await wrapper.get('[data-testid="connector-save"]').trigger('click'); expect(wrapper.emitted('save')?.[0]?.[0]).toEqual(configuration);
      const selects = wrapper.findAllComponents(ElSelect); selects[1]!.vm.$emit('update:modelValue', 'count'); await flushPromises();
      expect(wrapper.get('[data-testid="connector-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('目录读取失败、版本失效或只读时阻止保存', async () => {
    vi.mocked(listWorkflowConnectors).mockRejectedValueOnce(new Error('unavailable'));
    const wrapper = mount(WorkflowConnectorEditor, { props: { modelValue: configuration, fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); expect(wrapper.get('[data-testid="connector-save"]').attributes('disabled')).toBeDefined();
      expect(wrapper.find('[role="alert"]').exists()).toBe(true);
      await wrapper.setProps({ disabled: true }); expect(wrapper.get('[data-testid="connector-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('接通真实节点抽屉，保存后移除已失效的服务器绑定摘要', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'call', type: 6, ...configuration, boundConnectorHash: 'a'.repeat(64), childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, gatewayFields: fields, disabled: false, enabledNodeTypes: ['integration.trigger'] }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.findComponent(WorkflowConnectorEditor).exists()).toBe(true);
      wrapper.findComponent(WorkflowConnectorEditor).vm.$emit('save', configuration); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as typeof modelValue;
      expect(saved.childNode.connectorPolicy).toEqual(configuration.connectorPolicy); expect(saved.childNode).not.toHaveProperty('boundConnectorHash');
    } finally { wrapper.unmount(); }
  });
});
