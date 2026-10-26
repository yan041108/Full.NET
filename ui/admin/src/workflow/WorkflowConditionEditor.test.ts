import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { ElSelect } from 'element-plus';
import WorkflowConditionEditor from './WorkflowConditionEditor.vue';
import { readWorkflowCondition } from './workflow-conditions';
import { reactive } from 'vue';

const fields = [{ fieldKey: 'amount', fieldTypeKey: 'integer' as const, required: false, constraints: {} }];
describe('组合条件编辑', () => {
  it('将叶子切为明确的 AND 组，保留原条件', () => {
    const leaf = { fieldKey: 'amount', operator: 'greaterThan', value: 5 };
    const wrapper = mount(WorkflowConditionEditor, { props: { modelValue: leaf, fields } });
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 'and');
    expect(wrapper.emitted('update:modelValue')?.[0]?.[0]).toEqual({ operator: 'and', conditions: [leaf,
      { fieldKey: 'amount', operator: 'isNotEmpty' }] });
  });
  it('集合候选转换为已选字段的严格数字类型', () => {
    const wrapper = mount(WorkflowConditionEditor, { props: {
      modelValue: { fieldKey: 'amount', operator: 'in', value: [] }, fields
    } });
    wrapper.findAllComponents(ElSelect).at(-1)!.vm.$emit('update:modelValue', ['1', '2']);
    expect(wrapper.emitted('update:modelValue')?.[0]?.[0]).toEqual({ fieldKey: 'amount', operator: 'in', value: [1, 2] });
  });
  it('Vue 响应式集合可安全保存并切断引用', () => {
    const condition = reactive({ fieldKey: 'amount', operator: 'in', value: [1, 2] });
    const result = readWorkflowCondition(condition);
    expect(result.value).toEqual([1, 2]);
    condition.value.push(3);
    expect(result.value).toEqual([1, 2]);
  });
});
