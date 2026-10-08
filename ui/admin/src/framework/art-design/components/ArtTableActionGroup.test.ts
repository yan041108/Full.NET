import { describe, expect, it, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { defineComponent, h } from 'vue';
import { useAdminI18n } from '../../../i18n/adminI18n';
import ArtTableActionButton from './ArtTableActionButton.vue';
import ArtTableActionGroup from './ArtTableActionGroup.vue';

describe('ArtTableActionGroup', () => {
  beforeEach(() => {
    useAdminI18n().setLocale('zh-CN');
  });

  it.each([4, 0])('复用行组件时直接操作或更多菜单更新任务身份和点击目标（上限 %i）', async maxVisible => {
    const clicked: string[] = [];
    const Parent = defineComponent({
      props: { taskId: { type: String, required: true } },
      setup: props => () => {
        // 模拟表格更新时为复用组件提供新行闭包，旧行对象本身不会发生响应式变更。
        const taskId = props.taskId;
        return h(ArtTableActionGroup, { maxVisible }, { default: () => h('button', {
          'data-task-id': taskId, onClick: () => clicked.push(taskId)
        }, taskId) });
      }
    });
    const wrapper = mount(Parent, { props: { taskId: 'old' }, global: { stubs: {
      ElDropdown: { template: '<div><slot /><slot name="dropdown" /></div>' }, ElIcon: true
    } } });
    await wrapper.setProps({ taskId: 'new' });
    expect(wrapper.get('[data-task-id]').attributes('data-task-id')).toBe('new');
    await wrapper.get('[data-task-id]').trigger('click');
    expect(clicked).toEqual(['new']);
    wrapper.unmount();
  });

  it('超过 4 个操作时显示更多按钮', () => {
    const wrapper = mount(ArtTableActionGroup, {
      slots: {
        default: () => Array.from({ length: 5 }, (_, index) => hAction(`action-${index + 1}`))
      },
      global: {
        stubs: {
          ElDropdown: {
            template: '<div><slot /><div class="dropdown-panel"><slot name="dropdown" /></div></div>'
          },
          ElIcon: true
        }
      }
    });

    expect(wrapper.find('[data-testid="art-table-action-more"]').exists()).toBe(true);
    expect(wrapper.findAll('.art-table-action-group > [data-testid^="action-"]')).toHaveLength(4);
  });

  it('不超过上限时不显示更多按钮', () => {
    const wrapper = mount(ArtTableActionGroup, {
      slots: {
        default: () => [
          hAction('action-1'),
          hAction('action-2')
        ]
      },
      global: {
        stubs: {
          ElDropdown: true,
          ElIcon: true
        }
      }
    });

    expect(wrapper.find('[data-testid="art-table-action-more"]').exists()).toBe(false);
  });
});

function hAction(testId: string) {
  return h(ArtTableActionButton, {
    type: 'edit',
    testId,
    title: testId,
    onClick: () => undefined
  });
}
