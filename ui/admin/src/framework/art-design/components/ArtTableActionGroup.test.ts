import { describe, expect, it, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { h } from 'vue';
import { useAdminI18n } from '../../../i18n/adminI18n';
import ArtTableActionButton from './ArtTableActionButton.vue';
import ArtTableActionGroup from './ArtTableActionGroup.vue';

describe('ArtTableActionGroup', () => {
  beforeEach(() => {
    useAdminI18n().setLocale('zh-CN');
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
