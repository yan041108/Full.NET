import { flushPromises, mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it } from 'vitest';
import { readWorkflowDelayPolicy } from './workflow-delay-policy';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
import WorkflowDelayPolicyEditor from './WorkflowDelayPolicyEditor.vue';
import { defaultWorkflowWorkCalendar } from './workflow-work-calendar';

const relative = { modeKey: 'relative', timeZoneKey: 'UTC', duration: 2, unitKey: 'minute' };
const working = { modeKey: 'working', timeZoneKey: 'UTC', duration: 2, unitKey: 'hour', calendarVersionId: '019a23f0-0000-7000-8000-000000000001' };
describe('durable workflow delay', () => {
  it.each([
    relative,
    working,
    { ...working, boundCalendarHash: 'a'.repeat(64), boundCalendar: { ...defaultWorkflowWorkCalendar(), timeZoneRules: 'server-owned-rules' } },
    { modeKey: 'absolute', timeZoneKey: 'Asia/Shanghai', at: '2026-10-26T08:00:00+08:00' },
    { modeKey: 'absolute', timeZoneKey: 'UTC', fieldKey: 'deadline' }
  ])('延时配置双向往返保留后继和原时区 %j', policy => {
    expect(readWorkflowDelayPolicy(policy)).toEqual(policy);
    const root = { id: 'start', type: 0, nodeName: 'start', workflowKindKey: 'automation', childNode: { id: 'delay', type: 7, nodeName: 'delay', delayPolicy: policy, childNode: null } };
    const draft = fromWorkflowVue3Tree(root); const returned = fromWorkflowVue3Tree(toWorkflowVue3Tree(draft));
    expect(returned).toEqual(draft);
    expect(draft.nodes.find(node => node.nodeKey === 'delay')?.config).toEqual({ nodeName: 'delay', delayPolicy: policy, nextNodeKeys: ['end'] });
  });

  it.each([
    { ...relative, duration: 0 }, { ...relative, duration: 367, unitKey: 'day' }, { ...relative, duration: 1.5 },
    { ...relative, timeZoneKey: 'not-a-zone' }, { ...relative, calendarId: 'mutable' },
    { ...relative, unitKey: 'business_day' }, { ...relative, at: '2026-10-26T00:00:00Z' },
    { modeKey: 'absolute', timeZoneKey: 'UTC', at: '2026-10-26T00:00:00' },
    { modeKey: 'absolute', timeZoneKey: 'UTC', at: '2026-02-31T00:00:00Z' },
    { modeKey: 'absolute', timeZoneKey: 'UTC', at: '2026-10-26T00:00:00+14:01' },
    { modeKey: 'absolute', timeZoneKey: 'UTC', at: '2026-10-26T00:00:00Z', fieldKey: 'deadline' }
  ])('拒绝未知、歧义或越界配置 %j', value => expect(() => readWorkflowDelayPolicy(value)).toThrow('client.invalid_workflow_delay_policy'));

  it.each([
    { ...working, calendarVersionId: '00000000-0000-0000-0000-000000000000' },
    { ...working, calendarId: 'mutable' }, { ...working, duration: 367, unitKey: 'day' },
    { ...working, boundCalendarHash: 'a'.repeat(64) },
    { ...working, boundCalendarHash: 'invalid', boundCalendar: { ...defaultWorkflowWorkCalendar(), timeZoneRules: 'rules' } },
    { ...working, boundCalendarHash: 'a'.repeat(64), boundCalendar: { ...defaultWorkflowWorkCalendar(), timeZoneKey: 'Asia/Shanghai', timeZoneRules: 'rules' } }
  ])('工作时长拒绝可变日历或损坏绑定 %j', value => expect(() => readWorkflowDelayPolicy(value)).toThrow('client.invalid_workflow_delay_policy'));

  it('未绑定日期时间字段不能保存，合法保存保留独立等待配置', async () => {
    const wrapper = mount(WorkflowDelayPolicyEditor, { props: { fields: [], disabled: false,
      modelValue: { modeKey: 'absolute', timeZoneKey: 'UTC', fieldKey: 'deadline' } } });
    try {
      expect(wrapper.get('[data-testid="delay-save"]').attributes('disabled')).toBeDefined();
      await wrapper.setProps({ fields: [{ fieldKey: 'deadline', fieldTypeKey: 'datetime', required: false, constraints: {} }] });
      await wrapper.get('[data-testid="delay-save"]').trigger('click');
      expect(wrapper.emitted('save')).toEqual([[{ modeKey: 'absolute', timeZoneKey: 'UTC', fieldKey: 'deadline' }]]);
    } finally { wrapper.unmount(); }
  });

  it('真实添加按钮创建可回显延时，抽屉保存不丢失后继', async () => {
    const wrapper = mount(WorkflowVue3Designer, { attachTo: document.body, props: { modelValue: { id: 'start', type: 0, childNode: null }, disabled: false,
      enabledNodeTypes: ['start', 'end', 'timer.delay'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.get('.add-node-btn .btn').trigger('click'); await flushPromises();
      const add = document.body.querySelector<HTMLButtonElement>('button[aria-label="延时"]');
      expect(add).not.toBeNull(); add!.click(); await flushPromises();
      const model = wrapper.emitted('update:modelValue')!.at(-1)![0] as { childNode: { type: number; delayPolicy: unknown } };
      expect(model.childNode.type).toBe(7); expect(model.childNode.delayPolicy).toEqual({ ...relative, duration: 1 });
      await wrapper.findAll('.node-wrap-box').at(-1)!.trigger('click'); await flushPromises();
      const editor = wrapper.findComponent(WorkflowDelayPolicyEditor); expect(editor.exists()).toBe(true);
      editor.vm.$emit('save', { ...relative, duration: 3 }); await flushPromises();
      expect((wrapper.emitted('update:modelValue')!.at(-1)![0] as typeof model).childNode.delayPolicy).toEqual({ ...relative, duration: 3 });
    } finally { wrapper.unmount(); }
  });
});
