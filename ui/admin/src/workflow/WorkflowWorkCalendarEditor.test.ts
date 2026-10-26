import { mount } from '@vue/test-utils';
import { ElInput } from 'element-plus';
import { describe, expect, it } from 'vitest';
import WorkflowWorkCalendarEditor from './WorkflowWorkCalendarEditor.vue';
import { defaultWorkflowWorkCalendar, readWorkflowWorkCalendar } from './workflow-work-calendar';

describe('工作日历编辑', () => {
  it('时段编辑不修改原输入，支持午夜结束，例外日期未填时提示', async () => {
    const original = defaultWorkflowWorkCalendar();
    const wrapper = mount(WorkflowWorkCalendarEditor, { props: { modelValue: original, disabled: false } });
    try {
      wrapper.findAllComponents(ElInput)[2]!.vm.$emit('update:modelValue', '24:00');
      const value = wrapper.emitted('update:modelValue')!.at(-1)![0] as typeof original;
      expect(original.weekdays[0]!.ranges[0]!.end).toBe('17:00');
      expect(value.weekdays[0]!.ranges[0]!.end).toBe('24:00');
      expect(readWorkflowWorkCalendar(value)).toEqual(value);
      await wrapper.setProps({ modelValue: value });
      await wrapper.get('[data-testid="work-calendar-add-exception"]').trigger('click');
      await wrapper.setProps({ modelValue: wrapper.emitted('update:modelValue')!.at(-1)![0] as typeof original });
      expect(wrapper.find('[role="alert"]').exists()).toBe(true);
    } finally { wrapper.unmount(); }
  });

  it('只读日历不因子控件发出更新而改变', () => {
    const wrapper = mount(WorkflowWorkCalendarEditor, { props: { modelValue: defaultWorkflowWorkCalendar(), disabled: true } });
    try {
      wrapper.findComponent(ElInput).vm.$emit('update:modelValue', 'Asia/Shanghai');
      expect(wrapper.emitted('update:modelValue')).toBeUndefined();
    } finally { wrapper.unmount(); }
  });
});
