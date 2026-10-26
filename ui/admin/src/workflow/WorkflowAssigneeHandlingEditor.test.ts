import { mount } from '@vue/test-utils';
import { ElSelect } from 'element-plus';
import { describe, expect, it } from 'vitest';
import WorkflowAssigneeHandlingEditor from './WorkflowAssigneeHandlingEditor.vue';
import { fromWorkflowVue3Tree, readAssigneeHandling, toWorkflowVue3Tree, type WorkflowAssigneeHandling } from './workflow-vue3-adapter';

const id = '01912345-6789-7abc-8def-0123456789ab';
describe('无人及本人处理', () => {
  it('按配置往返替补顺序，拒绝嵌套、动态来源和超过三次的替补', () => {
    const policy = { sources: [{ resolverKindKey: 'initiator' }], handling: { whenInitiator: { actionKey: 'assign', fallbackPolicies: [
      { sources: [{ resolverKindKey: 'role_members', roleIds: [id] }] }, { sources: [{ resolverKindKey: 'initiator_primary_unit_leader' }] }
    ] } } };
    const tree = { id: 'start', type: 0, childNode: { id: 'review', type: 1, assigneePolicy: policy, childNode: null } };
    expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
    expect(() => readAssigneeHandling({ whenEmpty: { actionKey: 'assign', fallbackPolicies: Array(4).fill({ sources: [{ resolverKindKey: 'initiator' }] }) } })).toThrow();
    expect(() => readAssigneeHandling({ whenEmpty: { actionKey: 'assign', fallbackPolicies: [{ sources: [{ resolverKindKey: 'form_users', fieldKey: 'users' }] }] } })).toThrow();
    expect(() => readAssigneeHandling({ whenEmpty: { actionKey: 'assign', fallbackPolicies: [policy] } })).toThrow();
  });

  it('替补编辑不修改原始配置，明确切换来源后清除旧身份字段', async () => {
    const original: WorkflowAssigneeHandling = { whenEmpty: { actionKey: 'assign', fallbackPolicies: [{ sources: [{ resolverKindKey: 'role_members', roleIds: [id] }] }] } };
    const wrapper = mount(WorkflowAssigneeHandlingEditor, { props: { modelValue: original, users: [], roles: [], units: [], positions: [] } });
    const source = wrapper.findAllComponents(ElSelect).find(select => select.props('modelValue') === 'role_members')!;
    source.vm.$emit('update:modelValue', 'initiator_primary_unit_leader');
    const next = wrapper.emitted('update:modelValue')!.at(-1)![0] as WorkflowAssigneeHandling;
    expect(next.whenEmpty!.fallbackPolicies![0]!.sources[0]).toEqual({ resolverKindKey: 'initiator_primary_unit_leader' });
    expect(original.whenEmpty!.fallbackPolicies![0]!.sources[0]).toEqual({ resolverKindKey: 'role_members', roleIds: [id] });
    await wrapper.setProps({ modelValue: next });
    for (let count = 1; count < 3; count++) {
      await wrapper.get('[data-testid="handling-add-whenEmpty"]').trigger('click');
      await wrapper.setProps({ modelValue: wrapper.emitted('update:modelValue')!.at(-1)![0] as WorkflowAssigneeHandling });
    }
    expect(wrapper.get('[data-testid="handling-add-whenEmpty"]').attributes('disabled')).toBeDefined();
    wrapper.unmount();
  });

  it('恢复两个默认动作会移除显式处理配置', async () => {
    const wrapper = mount(WorkflowAssigneeHandlingEditor, { props: { modelValue: { whenInitiator: { actionKey: 'skip' } }, users: [], roles: [], units: [], positions: [] } });
    wrapper.findAllComponents(ElSelect)[1]!.vm.$emit('update:modelValue', 'keep');
    expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toBeUndefined(); wrapper.unmount();
  });
});
