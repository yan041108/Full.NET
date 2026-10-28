import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it } from 'vitest';
import { ElInputNumber, ElSelect } from 'element-plus';
import WorkflowRouteEditor from './WorkflowRouteEditor.vue';
import type { WorkflowFormField } from '@fullnet/client-contracts';
const policy = { modeKey: 'rules', targets: [{ targetKey: 'retry', nodeKey: 'review', regionKey: 'round', maximumIterations: 3,
  condition: { fieldKey: 'amount', operator: 'greaterThan', value: 0 } }] };
const fields: WorkflowFormField[] = [{ fieldKey: 'amount', fieldTypeKey: 'integer', required: false, constraints: {} }];
const props = { modelValue: policy, fields,
  nodes: [{ key: 'review', label: 'Review' }], disabled: false };
describe('动态路由配置', () => {
  it('保存固定引用与迭代额度，不创建目标副本', async () => {
    const wrapper = mount(WorkflowRouteEditor, { props, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); wrapper.getComponent(ElInputNumber).vm.$emit('update:modelValue', 2); await flushPromises();
      await wrapper.get('form').trigger('submit');
      expect(wrapper.emitted('save')?.[0]?.[0]).toEqual({ ...policy, targets: [{ ...policy.targets[0], maximumIterations: 2 }] });
    } finally { wrapper.unmount(); }
  });
  it('无效目标与只读模式不发出保存', async () => {
    const wrapper = mount(WorkflowRouteEditor, { props: { ...props, nodes: [], disabled: true }, global: { plugins: [createPinia()] } });
    try {
      await wrapper.get('form').trigger('submit'); expect(wrapper.emitted('save')).toBeUndefined();
      expect(wrapper.get('[data-testid="workflow-save-route"]').attributes('disabled')).toBeDefined();
      await wrapper.setProps({ disabled: false }); await wrapper.get('form').trigger('submit'); expect(wrapper.emitted('save')).toBeUndefined();
      await wrapper.setProps({ nodes: props.nodes }); wrapper.findAllComponents(ElSelect)[0]!.vm.$emit('update:modelValue', 'decision_field'); await flushPromises();
      await wrapper.get('form').trigger('submit'); expect(wrapper.emitted('save')).toBeUndefined();
    } finally { wrapper.unmount(); }
  });
});
