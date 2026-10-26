import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';
import { previewWorkflowAssignees } from '../api/workflow-definitions';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';
import WorkflowConditionEditor from './WorkflowConditionEditor.vue';
import WorkflowAssigneeHandlingEditor from './WorkflowAssigneeHandlingEditor.vue';

describe('活动截止设计配置', () => {
  it('监听器通过实际抽屉添加保存，禁用后不能继续改写', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'review', type: 1, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      await wrapper.get('[data-testid="workflow-listener-add"]').trigger('click'); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as { childNode: { listeners: unknown[] } };
      expect(saved.childNode.listeners).toEqual([{ listenerKey: 'listener1', eventKey: 'completed', actionKey: 'notify.initiator', actionVersion: 1 }]);
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      await wrapper.setProps({ disabled: true });
      expect(wrapper.get('[data-testid="workflow-listener-add"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });
  it('结果字段选择保存明确写权限，保留其他字段策略和后继', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'review', type: 1,
      fieldPolicies: { title: 'hidden' }, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false,
      gatewayFields: [{ fieldKey: 'accepted', fieldTypeKey: 'switch', required: false, constraints: {} }, { fieldKey: 'title', fieldTypeKey: 'text', required: false, constraints: {} }] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-result-callback-field"]') as VueWrapper).vm.$emit('update:modelValue', 'accepted'); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as { childNode: { resultCallbackFieldKey: string; fieldPolicies: Record<string, string>; childNode: null } };
      expect(saved.childNode.resultCallbackFieldKey).toBe('accepted'); expect(saved.childNode.fieldPolicies).toEqual({ title: 'hidden', accepted: 'editable' });
      expect(saved.childNode.childNode).toBeNull();
    } finally { wrapper.unmount(); }
  });

  it('只读目标不能被结果配置自动升级，禁用后保存事件不能提交', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'review', type: 1,
      fieldPolicies: { accepted: 'readOnly' }, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false,
      gatewayFields: [{ fieldKey: 'accepted', fieldTypeKey: 'switch', required: false, constraints: {} }] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-result-callback-field"]') as VueWrapper).vm.$emit('update:modelValue', 'accepted'); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')!.at(-1)).toEqual(['client.invalid_workflow_result_callback']);
      expect(modelValue.childNode.fieldPolicies.accepted).toBe('readOnly');
      await wrapper.setProps({ disabled: true }); await flushPromises();
      expect(wrapper.get('[data-testid="workflow-timeout-save"]').attributes('disabled')).toBeDefined();
    } finally { wrapper.unmount(); }
  });

  it('保存明确截止和暂停策略，保留原提醒与后继', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'review', type: 1,
      slaPolicy: { dueAfterMinutes: 60, pauseModeKey: 'freeze', actionKey: 'suspend' },
      timeoutPolicy: { dueAfterMinutes: 30, reminderIntervalMinutes: 10, maxReminderCount: 2 }, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      const control = wrapper.findComponent('[data-testid="workflow-sla-due"]') as VueWrapper;
      expect((control.props() as { modelValue: number }).modelValue).toBe(60);
      control.vm.$emit('update:modelValue', 120); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as typeof modelValue;
      expect(saved.childNode.slaPolicy).toEqual({ ...modelValue.childNode.slaPolicy, dueAfterMinutes: 120 });
      expect(saved.childNode.timeoutPolicy).toEqual(modelValue.childNode.timeoutPolicy);
    } finally { wrapper.unmount(); }
  });
});

vi.mock('../api/workflow-definitions', () => ({
  listWorkflowPositionCandidates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 }),
  listWorkflowRoleCandidates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 }),
  listWorkflowOrganizationUnitCandidates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, total: 0 }),
  previewWorkflowAssignees: vi.fn().mockResolvedValue([]),
  listWorkflowRecipientCandidates: vi.fn().mockResolvedValue({
    items: [
      {
        id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
        username: 'finance',
        displayName: '财务'
      },
      {
        id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002',
        username: 'manager',
        displayName: '经理'
      },
      {
        id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003',
        username: 'director',
        displayName: '总监'
      }
    ],
    page: 1,
    pageSize: 100,
    total: 1
  })
}));

describe('WorkflowVue3Designer', () => {
  it('依赖编辑仅变更当前节点引用，切换外部模型关闭旧抽屉', async () => {
    const modelValue = { id: 'start', type: 0, childNode: { id: 'call', type: 11,
      fieldPolicies: { title: 'readOnly', count: 'editable' }, connectorPolicy: { connectorKey: 'credit.check', connectorVersion: 1, inputFields: { subject: 'title' }, outputFields: { count: 'score' } },
      childNode: { id: 'consumer', type: 22, awaitActivityNodeKeys: ['call'], childNode: null } } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false, enabledNodeTypes: ['start', 'control.empty', 'integration.async_trigger', 'end'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('[data-testid="workflow-edit-dependencies"]').at(-1)!.trigger('click'); await flushPromises();
      const selection = wrapper.findComponent('[data-testid="workflow-dependency-selection"]') as VueWrapper;
      expect((selection.props() as { modelValue: unknown }).modelValue).toEqual(['call']);
      selection.vm.$emit('update:modelValue', []); await nextTick();
      await wrapper.get('[data-testid="workflow-save-dependencies"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as typeof modelValue;
      expect(saved.childNode.childNode).not.toHaveProperty('awaitActivityNodeKeys');
      expect(saved.childNode.connectorPolicy).toEqual(modelValue.childNode.connectorPolicy);
      await wrapper.findAll('[data-testid="workflow-edit-dependencies"]').at(-1)!.trigger('click'); await flushPromises();
      await wrapper.setProps({ modelValue: { id: 'start', type: 0, childNode: null } }); await flushPromises();
      expect(wrapper.findComponent('[data-testid="workflow-dependency-selection"]').exists()).toBe(false);
    } finally { wrapper.unmount(); }
  });

  it('保存本人跳过和替补策略时保留来源，关闭不适用的静态预览', async () => {
    const policy = { sources: [{ resolverKindKey: 'initiator' }], handling: { whenInitiator: { actionKey: 'skip' } } };
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'review', type: 1, assigneePolicy: policy, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.findComponent(WorkflowAssigneeHandlingEditor).props('modelValue')).toEqual(policy.handling);
      expect(wrapper.get('[data-testid="workflow-assignee-preview"]').attributes('disabled')).toBeDefined();
      wrapper.findComponent(WorkflowAssigneeHandlingEditor).vm.$emit('update:modelValue', { whenEmpty: { actionKey: 'suspend' }, whenInitiator: { actionKey: 'skip' } });
      await nextTick(); await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as typeof modelValue;
      expect(saved.childNode.assigneePolicy).toEqual({ ...policy, handling: { whenEmpty: { actionKey: 'suspend' }, whenInitiator: { actionKey: 'skip' } } });
    } finally { wrapper.unmount(); }
  });

  it('发起人自选编辑人数和范围时保留稳定选择键，不提前解析成员', async () => {
    const modelValue = { id: 'start', type: 0, childNode: { id: 'task', type: 18, taskPolicy: { completionModeKey: 'all' },
      assigneePolicy: { sources: [{ resolverKindKey: 'starter_selected_users', selectionPolicy: {
        selectionKey: 'review', modeKey: 'single', maximumUsers: 1, range: { kindKey: 'all' } } }] }, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.get('[data-testid="workflow-assignee-preview"]').attributes('disabled')).toBeDefined();
      (wrapper.findComponent('[data-testid="workflow-starter-selection-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'multiple'); await nextTick();
      (wrapper.findComponent('[data-testid="workflow-starter-selection-maximum"]') as VueWrapper).vm.$emit('update:modelValue', 3); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ childNode: { assigneePolicy: { sources: [{
        resolverKindKey: 'starter_selected_users', selectionPolicy: { selectionKey: 'review', modeKey: 'multiple', maximumUsers: 3, range: { kindKey: 'all' } }
      }] } } });
    } finally { wrapper.unmount(); }
  });
  it.each(['form_users', 'form_units'])('动态来源 %s 编辑保留发布字段，运行前不伪造目录预览', async kind => {
    const id = '01912345-6789-7abc-8def-0123456789ab';
    const modelValue = { id: 'start', type: 0, childNode: { id: 'task', type: 18, taskPolicy: { completionModeKey: 'all' },
      assigneePolicy: { sources: [{ resolverKindKey: kind, fieldKey: 'refs' }] }, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false, gatewayFields: [
      { fieldKey: 'refs', fieldTypeKey: 'select', required: false, constraints: { options: [id] } },
      { fieldKey: 'refs2', fieldTypeKey: 'checkbox', required: false, constraints: { options: [id] } }] }, global: { plugins: [createPinia()] } });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect((wrapper.findComponent('[data-testid="workflow-assignee-field"]') as VueWrapper).props()).toMatchObject({ modelValue: 'refs' });
      expect(wrapper.get('[data-testid="workflow-assignee-preview"]').attributes('disabled')).toBeDefined();
      (wrapper.findComponent('[data-testid="workflow-assignee-field"]') as VueWrapper).vm.$emit('update:modelValue', 'refs2'); await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ childNode: { assigneePolicy: { sources: [{ resolverKindKey: kind, fieldKey: 'refs2' }] } } });
    } finally { wrapper.unmount(); }
  });

  it('撤回设置先完成编辑再保存到开始节点，不改变人工动作策略', async () => {
    const modelValue = { id: 'start', type: 0, childNode: { id: 'review', type: 1, childNode: null } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.get('[data-testid="workflow-withdrawal-open"]').trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-withdrawal-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'at_active_nodes'); await nextTick();
      (wrapper.findComponent('[data-testid="workflow-withdrawal-nodes"]') as VueWrapper).vm.$emit('update:modelValue', ['review']); await nextTick();
      await wrapper.get('[data-testid="workflow-withdrawal-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as Record<string, unknown>;
      expect(saved.withdrawalPolicy).toEqual({ modeKey: 'at_active_nodes', nodeKeys: ['review'] });
    } finally { wrapper.unmount(); }
  });
  it('领取单独编辑释放开关，不产生审批策略', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'claim', type: 17,
      childNode: null, claimPolicy: { allowRelease: true }, assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] } } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false, enabledNodeTypes: ['start', 'human.claim', 'end'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-approval-mode"]').exists()).toBe(false);
      (wrapper.findComponent('[data-testid="workflow-claim-release"]') as VueWrapper).vm.$emit('update:modelValue', false);
      await nextTick(); await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as { childNode: Record<string, unknown> };
      expect(saved.childNode.claimPolicy).toEqual({ allowRelease: false }); expect(saved.childNode.approvalPolicy).toBeUndefined();
    } finally { wrapper.unmount(); }
  });
  it('办理独立编辑提交完成规则，不产生审批策略或审批动作', async () => {
    const modelValue = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'task', type: 18, childNode: null,
      taskPolicy: { completionModeKey: 'all' }, assigneePolicy: { sources: [{ resolverKindKey: 'specified_users',
        userIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'] }] } } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false, enabledNodeTypes: ['start', 'human.task', 'end'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-approval-mode"]').exists()).toBe(false);
      (wrapper.findComponent('[data-testid="workflow-task-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'sequential');
      await nextTick(); await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as { childNode: Record<string, unknown> };
      expect(saved.childNode.taskPolicy).toEqual({ completionModeKey: 'sequential' });
      expect(saved.childNode.approvalPolicy).toBeUndefined();
      expect(saved.childNode.assigneePolicy).toEqual(modelValue.childNode.assigneePolicy);
    } finally { wrapper.unmount(); }
  });

  it('纯指定成员的组保存后不隐式加入发起人来源', async () => {
    const users = ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'];
    const wrapper = mount(WorkflowVue3Designer, { props: { disabled: false, modelValue: { id: 'start', type: 0,
      childNode: { id: 'review', type: 21, childNode: null, groups: [
        { groupKey: 'first', approvalPolicy: { modeKey: 'all', approverUserIds: users } },
        { groupKey: 'second', assigneePolicy: { sources: [{ resolverKindKey: 'specified_users', userIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003'] }] } }
      ], groupApprovalPolicy: { modeKey: 'all' } } } }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'any');
      await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as { childNode: { groups: Record<string, unknown>[] } };
      expect(saved.childNode.groups[0]?.assigneePolicy).toBeUndefined();
      expect(saved.childNode.groups[0]?.approvalPolicy).toEqual({ modeKey: 'any', approverUserIds: users });
    } finally { wrapper.unmount(); }
  });

  it('分组切换保留各自规则，保存后不产生根级普通审批策略', async () => {
    const modelValue = { id: 'start', type: 0, childNode: { id: 'review', type: 21, childNode: null,
      groups: [
        { groupKey: 'first', groupName: '首组', assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }], handling: { whenInitiator: { actionKey: 'skip' } } } },
        { groupKey: 'second', groupName: '次组', assigneePolicy: { sources: [{ resolverKindKey: 'specified_users', userIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'] }] } }
      ], groupApprovalPolicy: { modeKey: 'all' } } };
    const wrapper = mount(WorkflowVue3Designer, { props: { modelValue, disabled: false, enabledNodeTypes: ['start', 'human.group_approval', 'end'] }, global: { plugins: [createPinia()] } });
    try {
      await flushPromises(); await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect(wrapper.find('[data-testid="workflow-group-select"]').exists()).toBe(true);
      expect(wrapper.findComponent(WorkflowAssigneeHandlingEditor).props('modelValue')).toEqual({ whenInitiator: { actionKey: 'skip' } });
      (wrapper.findComponent('[data-testid="workflow-group-select"]') as VueWrapper).vm.$emit('update:modelValue', 1);
      await nextTick();
      expect((wrapper.findComponent('[data-testid="workflow-assignee-source-kind"]') as VueWrapper).props()).toMatchObject({ modelValue: 'specified_users' });
      expect(wrapper.findComponent(WorkflowAssigneeHandlingEditor).props('modelValue')).toBeUndefined();
      await wrapper.get('[data-testid="workflow-group-name"]').setValue('新组名');
      await nextTick(); await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as { childNode: Record<string, unknown> };
      expect(saved.childNode.groups).toMatchObject([{ groupKey: 'first', assigneePolicy: { handling: { whenInitiator: { actionKey: 'skip' } } } }, { groupKey: 'second', groupName: '新组名' }]);
      expect(saved.childNode.approvalPolicy).toBeUndefined(); expect(saved.childNode.assigneePolicy).toBeUndefined();
    } finally { wrapper.unmount(); }
  });

  it.each([
    [{ resolverKindKey: 'position_members', positionIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'] }, '岗位成员'],
    [{ resolverKindKey: 'organization_unit_members', unitId: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002' }, '部门成员'],
    [{ resolverKindKey: 'initiator_leader_chain', maximumLevels: 3 }, '连续多级主管'],
    [{ resolverKindKey: 'initiator_direct_manager' }, '发起人直属上级'],
    [{ resolverKindKey: 'flow_administrators' }, '流程管理员']
  ] as const)('纯组织来源可显示摘要、保存并回显，不强制添加用户：%j', async (source, summary) => {
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, administratorUserIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'], childNode: { id: 'review', type: 1, childNode: null,
        assigneePolicy: { sources: [source] }, approvalPolicy: { modeKey: 'all', approverUserIds: [] } } } },
      global: { plugins: [createPinia()] }
    });
    try {
      await flushPromises(); expect(wrapper.findAll('.node-wrap-box').at(1)!.text()).toContain(summary);
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'any');
      await nextTick(); await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as Record<string, unknown>;
      expect(saved.administratorUserIds).toEqual(['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002']);
      expect(saved).toMatchObject({ childNode: { assigneePolicy: { sources: [source] }, approvalPolicy: { modeKey: 'any', approverUserIds: [] } } });
      await wrapper.setProps({ modelValue: saved });
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      if (source.resolverKindKey === 'initiator_leader_chain')
        expect((wrapper.findComponent('[data-testid="workflow-assignee-maximum-levels"]') as VueWrapper).props()).toMatchObject({ modelValue: 3 });
    } finally { wrapper.unmount(); }
  });

  it('多个来源逐条编辑并保留顺序，明确发起人不降为默认', async () => {
    const sources = [{ resolverKindKey: 'initiator' },
      { resolverKindKey: 'initiator_ancestor_unit_leader', ancestorLevel: 2 }];
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, childNode: {
        id: 'review', type: 1, childNode: null, assigneePolicy: { sources }
      } } }, global: { plugins: [createPinia()] }
    });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
      await flushPromises();
      await wrapper.get('[data-testid="workflow-assignee-source-1"]').trigger('click');
      (wrapper.findComponent('[data-testid="workflow-assignee-ancestor-level"]') as VueWrapper).vm.$emit('update:modelValue', 3);
      await nextTick();
      await wrapper.get('[data-testid="workflow-assignee-source-0"]').trigger('click');
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
      await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ childNode: { assigneePolicy: {
        sources: [{ resolverKindKey: 'initiator' }, { resolverKindKey: 'initiator_ancestor_unit_leader', ancestorLevel: 3 }]
      } } });
      expect(sources[1]?.ancestorLevel).toBe(2);
    } finally { wrapper.unmount(); }
  });

  it('默认多人成员不注入发起人，显式添加和移除来源可回到默认', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, childNode: {
        id: 'review', type: 1, childNode: null, approvalPolicy: { modeKey: 'all',
          approverUserIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'] }
      } } }, global: { plugins: [createPinia()] }
    });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
      await flushPromises();
      await wrapper.get('[data-testid="workflow-assignee-add-source"]').trigger('click');
      await wrapper.get('[data-testid="workflow-assignee-remove-source"]').trigger('click');
      (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'any');
      await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
      await flushPromises();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as { childNode: Record<string, unknown> };
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      expect(saved).toBeDefined();
      expect(saved.childNode.assigneePolicy).toBeUndefined();
      expect(saved.childNode.approvalPolicy).toMatchObject({ modeKey: 'any' });
    } finally { wrapper.unmount(); }
  });

  it('依次审批保存回显并按实际来源追加成员预览完整顺序', async () => {
    const users = ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002'];
    const source = { sources: [{ resolverKindKey: 'specified_users', userIds: [users[0]] }] };
    vi.mocked(previewWorkflowAssignees).mockResolvedValueOnce(users.map((id, index) => ({ id, username: `user${index}`, displayName: `User ${index}` })));
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, childNode: {
        id: 'review', type: 1, nodeName: '依次审批', childNode: null, assigneePolicy: source,
        approvalPolicy: { modeKey: 'all', approverUserIds: users.slice(1) }
      } } }, global: { plugins: [createPinia()] }
    });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'sequential');
      await nextTick();
      await wrapper.get('[data-testid="workflow-assignee-preview"]').trigger('click'); await flushPromises();
      expect(previewWorkflowAssignees).toHaveBeenLastCalledWith(source, undefined, undefined, users.slice(1));
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click'); await flushPromises();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as Record<string, unknown>;
      expect(saved).toMatchObject({ childNode: { assigneePolicy: source, approvalPolicy: { modeKey: 'sequential', approverUserIds: users.slice(1) } } });
      await wrapper.setProps({ modelValue: saved });
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click'); await flushPromises();
      expect((wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).props()).toMatchObject({ modelValue: 'sequential' });
    } finally { wrapper.unmount(); }
  });

  it('比例审批保存回显实际百分比且数值控件约束零门槛', async () => {
    const users = ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001', '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002',
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003'];
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, nodeName: '发起人', childNode: {
        id: 'review', type: 1, nodeName: '审批人', childNode: null, settype: 1,
        approvalPolicy: { modeKey: 'all', approverUserIds: users }
      } } }, global: { plugins: [createPinia()] }
    });
    try {
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
      await flushPromises();
      (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper).vm.$emit('update:modelValue', 'percent');
      await nextTick();
      (wrapper.findComponent('[data-testid="workflow-approval-percentage"]') as VueWrapper).vm.$emit('update:modelValue', 67);
      await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
      await flushPromises();
      expect(wrapper.emitted('validation-error')).toBeUndefined();
      const saved = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as Record<string, unknown>;
      expect(saved).toMatchObject({ childNode: { approvalPolicy: {
        modeKey: 'percent', approverUserIds: users, requiredPercentage: 67
      } } });
      await wrapper.setProps({ modelValue: saved });
      await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
      await flushPromises();
      expect((wrapper.findComponent('[data-testid="workflow-approval-percentage"]') as VueWrapper).props()).toMatchObject({ modelValue: 67 });
      (wrapper.findComponent('[data-testid="workflow-approval-percentage"]') as VueWrapper).vm.$emit('update:modelValue', 0);
      await nextTick();
      await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
      await flushPromises();
      expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ childNode: {
        approvalPolicy: { modeKey: 'percent', requiredPercentage: 1 }
      } });
    } finally { wrapper.unmount(); }
  });

  it('条件抽屉只编辑选中分支并保留兄弟分支与公共后继', async () => {
    const conditions = [{ fieldKey: 'amount', operator: 'greaterThan', value: 1 }, { fieldKey: 'amount', operator: 'lessThan', value: 10 }];
    const sibling = { id: 'fallback', type: 3, priorityLevel: 2, isDefault: true,
      childNode: { id: 'fallback-human', type: 1, nodeName: '默认审批' } };
    const wrapper = mount(WorkflowVue3Designer, {
      props: { disabled: false, modelValue: { id: 'start', type: 0, childNode: {
        id: 'route', type: 10, conditionNodes: [
          { id: 'selected', type: 3, priorityLevel: 1, branchKey: 'selected', condition: { operator: 'and', conditions },
            childNode: { id: 'selected-human', type: 1, nodeName: '条件审批' } }, sibling
        ], childNode: { id: 'common', type: 1, nodeName: '公共审批' }
      } } }, global: { plugins: [createPinia()] }
    });
    await wrapper.get('.condition-node .content').trigger('click');
    await flushPromises();
    const editor = wrapper.findComponent(WorkflowConditionEditor);
    expect(editor.exists()).toBe(true);
    expect(editor.props('modelValue')).toEqual({ operator: 'and', conditions });
    editor.vm.$emit('update:modelValue', { operator: 'or', conditions });
    await nextTick();
    await wrapper.get('[data-testid="workflow-gateway-save"]').trigger('click');
    await flushPromises();
    expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ childNode: {
      conditionNodes: [{ id: 'selected', condition: { operator: 'or', conditions }, childNode: { id: 'selected-human' } }, sibling],
      childNode: { id: 'common' }
    } });
    wrapper.unmount();
  });

  it.each([[10, 15], [12, 8], [13, 8]])('类型 %i 达到 %i 条分支后禁止继续添加和复制', async (type, maximum) => {
    const conditionNodes = Array.from({ length: maximum - 1 }, (_, index) => ({
      id: `condition-${index}`, type: 3, nodeName: `条件${index}`, branchKey: `branch-${index}`,
      fieldKey: 'amount', operator: 'equals', value: index, priorityLevel: index + 1,
      childNode: { id: `approval-${index}`, type: 1, nodeName: '审批人', childNode: null }
    }));
    if (type !== 12) {
      conditionNodes.push({
        id: 'default', type: 3, nodeName: '默认分支', branchKey: 'fallback',
        fieldKey: 'amount', operator: 'equals', value: 0, priorityLevel: maximum,
        childNode: { id: 'fallback-approval', type: 1, nodeName: '审批人', childNode: null },
        ...{ isDefault: true }
      });
    }
    const wrapper = mount(WorkflowVue3Designer, {
      props: {
        disabled: false,
        modelValue: { id: 'start', type: 0, nodeName: '发起人', childNode: {
          id: 'route', type, nodeName: '路由', conditionNodes, childNode: null
        } }
      },
      global: { plugins: [createPinia()] }
    });
    await nextTick();
    const initial = conditionNodes.length;
    await wrapper.get('.add-branch').trigger('click');
    expect(wrapper.findAll('.branch-box > .col-box')).toHaveLength(initial + 1);
    expect(wrapper.get<HTMLButtonElement>('.add-branch').element.disabled).toBe(true);
    expect(wrapper.findAll<HTMLButtonElement>('.copy-branch').every(button => button.element.disabled)).toBe(true);
    await wrapper.get('.add-branch').trigger('click');
    await wrapper.get('.copy-branch').trigger('click');
    expect(wrapper.findAll('.branch-box > .col-box')).toHaveLength(initial + 1);
    wrapper.unmount();
  });

  it('初始化外部流程树时不反向回写，避免父子双向绑定递归更新', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      props: {
        disabled: false,
        modelValue: {
          id: 'start',
          type: 0,
          nodeName: '发起人',
          childNode: null
        }
      },
      global: { plugins: [createPinia()] }
    });

    await nextTick();

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });

  it('真实挂载复制设计器时不依赖宿主的 Element Plus 全局注册', () => {
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const wrapper = mount(WorkflowVue3Designer, {
        props: {
          disabled: false,
          modelValue: {
            id: 'start',
            type: 0,
            nodeName: '发起人',
            childNode: null
          }
        },
        global: { plugins: [createPinia()] }
      });

      expect(wrapper.find('[data-testid="workflow-vue3-designer"]').exists()).toBe(true);
      expect(warning.mock.calls.flat().join('\n')).not.toContain('Failed to resolve component: el-');
    } finally {
      warning.mockRestore();
    }
  });

  it('不创建当前服务端尚不可执行的抄送节点入口', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        enabledNodeTypes: ['start', 'human.approval', 'end'],
        modelValue: {
          id: 'start',
          type: 0,
          nodeName: '发起人',
          childNode: null
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.get('.add-node-btn .btn').trigger('click');
    await nextTick();

    expect(document.body.querySelector('.add-node-popover-item.approver')).not.toBeNull();
    expect(document.body.querySelector('.add-node-popover-item.notifier')).toBeNull();
    wrapper.unmount();
  });

  it('服务端启用抄送后提供活动用户选择抽屉', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        enabledNodeTypes: ['start', 'human.approval', 'notify.cc', 'end'],
        modelValue: {
          id: 'start',
          type: 0,
          nodeName: '发起人',
          childNode: null
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.get('.add-node-btn .btn').trigger('click');
    await nextTick();
    const notifier = document.body.querySelector<HTMLButtonElement>(
      '.add-node-popover-item.notifier'
    );
    expect(notifier).not.toBeNull();
    notifier!.click();
    await nextTick();
    await wrapper.findAll('.node-wrap-box').at(-1)!.trigger('click');
    await flushPromises();

    expect(document.body.querySelector('[data-testid="workflow-cc-recipient-select"]'))
      .not.toBeNull();
    wrapper.unmount();
  });

  it('服务端启用排他网关后提供条件分支入口', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        enabledNodeTypes: ['start', 'human.approval', 'gateway.exclusive', 'end'],
        gatewayFields: [{
          fieldKey: 'amount',
          fieldTypeKey: 'money',
          required: true,
          constraints: { scale: 2 }
        }],
        modelValue: {
          id: 'start',
          type: 0,
          nodeName: '发起人',
          childNode: null
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.get('.add-node-btn .btn').trigger('click');
    await nextTick();
    const gateway = document.body.querySelector<HTMLButtonElement>(
      '.add-node-popover-item.condition'
    );
    expect(gateway).not.toBeNull();
    gateway!.click();
    await nextTick();

    expect(wrapper.find('.branch-wrap').exists()).toBe(true);
    wrapper.unmount();
  });

  it('审批节点提供闭合的超时、催办与升级配置抽屉', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        modelValue: {
          id: 'start', type: 0, nodeName: '发起人', childNode: {
            id: 'approve', type: 1, nodeName: '审批人', childNode: null
          }
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
    await nextTick();
    expect(document.body.querySelector('[data-testid="workflow-timeout-enabled"]')).not.toBeNull();
    await wrapper.get('[data-testid="workflow-timeout-enabled"] input').setValue(true);
    await nextTick();
    expect(document.body.querySelector('[data-testid="workflow-timeout-due"]')).not.toBeNull();
    expect(document.body.querySelector('[data-testid="workflow-timeout-reminder-count"]')).not.toBeNull();
    wrapper.unmount();
  });

  it('审批抽屉保存合法的 N-of-M 办理人和票数门槛', async () => {
    const approvers = [
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d002',
      '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003'
    ];
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        modelValue: {
          id: 'start', type: 0, nodeName: '发起人', childNode: {
            id: 'approve', type: 1, nodeName: '审批人', childNode: null,
            settype: 1,
            nodeUserList: approvers.map((id, index) => ({
              id,
              name: ['财务', '经理', '总监'][index],
              type: 'user'
            })),
            approvalPolicy: {
              modeKey: 'all',
              approverUserIds: approvers
            }
          }
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
    await flushPromises();
    (wrapper.findComponent('[data-testid="workflow-approval-mode"]') as VueWrapper)
      .vm.$emit('update:modelValue', 'nOfM');
    await nextTick();
    (wrapper.findComponent('[data-testid="workflow-approval-required"]') as VueWrapper)
      .vm.$emit('update:modelValue', 2);
    await nextTick();
    await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
    await flushPromises();

    expect(wrapper.emitted('validation-error')).toBeUndefined();
    const updates = wrapper.emitted('update:modelValue');
    expect(updates).toBeDefined();
    const latest = updates!.at(-1)![0] as {
      childNode?: { approvalPolicy?: Record<string, unknown> }
    };
    expect(latest.childNode?.approvalPolicy).toEqual({
      modeKey: 'nOfM',
      approverUserIds: approvers,
      requiredApprovals: 2
    });
    wrapper.unmount();
  });

  it('审批抽屉拒绝只有一个办理人的多人策略并保持开启', async () => {
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        modelValue: {
          id: 'start', type: 0, nodeName: '发起人', childNode: {
            id: 'approve', type: 1, nodeName: '审批人', childNode: null,
            settype: 1,
            nodeUserList: [{
              id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
              name: '财务',
              type: 'user'
            }],
            approvalPolicy: {
              modeKey: 'all',
              approverUserIds: ['019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001']
            }
          }
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-timeout-save"]').trigger('click');
    await nextTick();

    expect(wrapper.emitted('validation-error')).toEqual([
      ['client.invalid_workflow_approval_policy']
    ]);
    expect(document.body.querySelector('[data-testid="workflow-approval-mode"]')).not.toBeNull();
    wrapper.unmount();
  });

  it('办理人预览接受只读候选数组并渲染显示名', async () => {
    vi.mocked(previewWorkflowAssignees).mockResolvedValueOnce(Object.freeze([
      Object.freeze({
        id: '019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d001',
        username: 'finance',
        displayName: '财务'
      })
    ]));
    const wrapper = mount(WorkflowVue3Designer, {
      attachTo: document.body,
      props: {
        disabled: false,
        modelValue: {
          id: 'start', type: 0, nodeName: '发起人', childNode: {
            id: 'approve', type: 1, nodeName: '审批人', childNode: null
          }
        }
      },
      global: { plugins: [createPinia()] }
    });

    await wrapper.findAll('.node-wrap-box').at(1)!.trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-assignee-preview"]').trigger('click');
    await flushPromises();

    expect(previewWorkflowAssignees).toHaveBeenCalled();
    expect(wrapper.get('.workflow-assignee-preview__users').text()).toContain('财务 (finance)');
    wrapper.unmount();
  });
});
