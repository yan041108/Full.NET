import { mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';
import WorkflowVue3Designer from './WorkflowVue3Designer.vue';

describe('WorkflowVue3Designer', () => {
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
});
