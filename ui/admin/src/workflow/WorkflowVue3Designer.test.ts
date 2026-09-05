import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';

vi.mock('../api/workflow-definitions', () => ({
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
});
