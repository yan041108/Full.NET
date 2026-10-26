import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { ElSelect } from 'element-plus';
import { describe, expect, it } from 'vitest';
import WorkflowFieldMutationEditor from './WorkflowFieldMutationEditor.vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
import type { WorkflowFormField } from '@fullnet/client-contracts';

const fields: WorkflowFormField[] = [
  { fieldKey: 'first', fieldTypeKey: 'text', required: false, constraints: {} },
  { fieldKey: 'second', fieldTypeKey: 'text', required: false, constraints: {} },
  { fieldKey: 'must', fieldTypeKey: 'text', required: true, constraints: {} }
];
const configuration = { fieldPolicies: { first: 'required', second: 'readOnly' }, mutationPolicy: { assignments: [{ fieldKey: 'first', sourceFieldKey: 'second' }] } };
describe('field activity editor', () => {
  it('keeps explicit required access and source copy when saving the real editor', async () => {
    const wrapper = mount(WorkflowFieldMutationEditor, { props: { modelValue: configuration, kind: 'data.modify', fields, disabled: false }, global: { plugins: [createPinia()] } });
    try { await flushPromises(); await wrapper.get('[data-testid="field-activity-save"]').trigger('click'); expect(wrapper.emitted('save')?.[0]?.[0]).toEqual(configuration); }
    finally { wrapper.unmount(); }
  });
  it('excludes schema-required clear targets and disables invalid and readonly saves', async () => {
    const wrapper = mount(WorkflowFieldMutationEditor, { props: { kind: 'data.clear', fields, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); expect(wrapper.get('[data-testid="field-activity-save"]').attributes('disabled')).toBeDefined();
      const select = wrapper.findComponent(ElSelect); select.vm.$emit('update:modelValue', 'first'); await flushPromises();
      expect(wrapper.get('[data-testid="field-activity-save"]').attributes('disabled')).toBeUndefined();
      await wrapper.get('[data-testid="field-activity-save"]').trigger('click'); expect(wrapper.emitted('save')?.[0]?.[0]).toEqual({ fieldPolicies: { first: 'editable' }, mutationPolicy: { fieldKeys: ['first'] } });
      await wrapper.setProps({ disabled: true }); expect(wrapper.get('[data-testid="field-activity-save"]').attributes('disabled')).toBeDefined();
      expect(select.text()).not.toContain('must');
    } finally { wrapper.unmount(); }
  });
  it('opens the real designer drawer and persists the closed field configuration', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'modify', type: 15, ...configuration, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, gatewayFields: fields, disabled: false, enabledNodeTypes: ['data.modify'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.findComponent(WorkflowFieldMutationEditor).exists()).toBe(true);
      const changed = { fieldPolicies: { first: 'required' }, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'Changed' }] } };
      wrapper.findComponent(WorkflowFieldMutationEditor).vm.$emit('save', changed); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined(); expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ ...modelValue, childNode: { ...modelValue.childNode, ...changed } });
    } finally { wrapper.unmount(); }
  });
});
